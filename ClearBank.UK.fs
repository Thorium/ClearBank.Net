module ClearBank.UK

open System
open System.Net.Http
open System.Net
open SwaggerProvider
open ClearBank.Common


// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]schemaV1 = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v1.json"
let [<Literal>]schemaV2 = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v2.json"
let [<Literal>]schemaV3Accounts = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v3-accounts.json"
let [<Literal>]schemaV3PaymentsFps = __SOURCE_DIRECTORY__ + @"/Schemas/fps-initiate-payment-v3.json"

type internal ClearBankSwaggerV1 = SwaggerClientProvider<schemaV1, PreferAsync=true>
type ClearBankSwaggerV2 = SwaggerClientProvider<schemaV2, PreferAsync=true>
type ClearBankOpenApiV3Accounts = OpenApiClientProvider<schemaV3Accounts, PreferAsync=true>
type FpsPaymentsV3 = OpenApiClientProvider<schemaV3PaymentsFps, PreferAsync=true>

type ModelsV3Accounts = ClearBankOpenApiV3Accounts.ClearBank.FI.API.Accounts.Versions.V3.Models
type AccountsV3 = ModelsV3Accounts.Binding.Accounts

let callTestEndpoint config azureKeyVaultCertificateName =

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
    let client = ClearBankSwaggerV1.Client httpClient
    async {

        let authToken = "Bearer " + config.PrivateKey
        let payload = System.Text.Json.JsonDocument.Parse("""{"institutionId": "string","body": "hello world!"}""") |> box
        let payloaStr = client.Serialize payload

        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName payloaStr |> Async.AwaitTask
        let requestId = Guid.NewGuid().ToString("N")

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestId config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.V1TestPost(authToken, signature_bodyhash_string, requestId, payload) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err

            //printfn "Used signature: %s" signature_bodyhash_string
            return Error(err, details)
    }

let ``account to string`` acc =
    match acc with
    | IBAN nr ->
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(iban = nr)
    | BBAN nr ->
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                FpsPaymentsV3.BatchCounterpartAccountGenericIdentification(
                    nr,
                    schemeName = FpsPaymentsV3.CounterpartAccountGenericIdentificationScheme(
                                    proprietary = "BBAN"
                                    )
                    //,"issuer"
                ))
    | UK_Domestic(sortcode, account) ->
        let identifier =
            "GBR" +
                sortcode.Replace(" ", "").Replace("-", "") +
                account.Replace(" ", "").Replace("-", "")
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                FpsPaymentsV3.BatchCounterpartAccountGenericIdentification(
                    identifier,
                    schemeName = FpsPaymentsV3.CounterpartAccountGenericIdentificationScheme(
                                    proprietary = "PRTY_COUNTRY_SPECIFIC"
                                    )
                    //,"issuer"
                ))

type PaymentTransfer = {
    To: BankAccount
    AccountHolder: string
    Sum: decimal
    Currency: string
    Description: string
    PaymentReference: string
    TransactionId: string
}

/// Creates credit transfer for createPaymentInstruction
let createCreditTransfer (payment:PaymentTransfer) =
    FpsPaymentsV3.BatchCreditTransfer(
        paymentIdentification = FpsPaymentsV3.BatchPaymentIdentification(
                                    payment.Description, // instructionIdentification
                                    payment.TransactionId // endToEndIdentification
                                ),

        amount = FpsPaymentsV3.BatchAmount(Convert.ToDouble(payment.Sum), payment.Currency),
        creditor = FpsPaymentsV3.BatchCreditorPartyIdentifier(payment.AccountHolder (*, "legalEntityIdentifier"*)),
        creditorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
            identification = ``account to string`` payment.To),
        remittanceInformation =
            FpsPaymentsV3.BatchRemittanceInformation(structured =
                FpsPaymentsV3.BatchStructured(creditorReferenceInformation =
                    FpsPaymentsV3.BatchCreditorReferenceInformation(payment.PaymentReference // reference

                )
            )
        )
    )

/// Creates payment instructions from createCreditTransfer for transferPayments
let createPaymentInstruction address legalEntityIdentifier batchId (account:ClearBank.Common.BankAccount) transfers =
    let req =
        FpsPaymentsV3.BatchPaymentInstruction(
            debtor = FpsPaymentsV3.BatchDebtorPartyIdentifier(
                address = address,
                legalEntityIdentifier = (legalEntityIdentifier |> Option.defaultValue "")),
            paymentInstructionIdentification = batchId,
            debtorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
                identification = ``account to string`` account
                ),
            creditTransfers = transfers
        )
    req

/// Creates a new account
let createNewAccount config azureKeyVaultCertificateName (requestId:Guid) (sortCode:string) accountName ownerName =
    let req =
        let owner = AccountsV3.PartyIdentification(ownerName)
        AccountsV3.CreateAccountRequest(accountName, owner, (sortCode.Replace("-", "")))
    let requestIdS = requestId.ToString("N") //todo, unique, save to db

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
    let client = ClearBankOpenApiV3Accounts.Client httpClient

    async {

        let authToken = "Bearer " + config.PrivateKey
        let toHash = client.Serialize req
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.V3InstitutionsByInstitutionIdAccountsPost(authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Get all the accounts
let getAccounts config =

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
    let client = ClearBankOpenApiV3Accounts.Client httpClient

    async {
        let authToken = "Bearer " + config.PrivateKey
        let! r = client.V3InstitutionsByInstitutionIdAccountsGet(authToken) |> Async.Catch
        httpClient.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Get all the transactions
let getTransactions config pageSize pageNumber startDate endDate =

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
    let client = ClearBankSwaggerV2.Client httpClient

    async {
        let authToken = "Bearer " + config.PrivateKey
        let! r = client.V2TransactionsGet(authToken, pageNumber, pageSize, startDate, endDate) |> Async.Catch
        httpClient.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Get a transaction with correct end-to-end-id
/// if you are lucky enough to own accountId (from getAccounts) and transactionId (from getTransactions)
let getAccountTransaction config (accountId:string) (transactionId:string) =

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
    let clientV2 = ClearBankSwaggerV2.Client httpClient

    async {
        let authToken = "Bearer " + config.PrivateKey
        let! r = clientV2.V2AccountsByAccountIdTransactionsByTransactionIdGet(accountId, transactionId, authToken) |> Async.Catch
        httpClient.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Post payments created with createPaymentInstruction
let transferPayments config azureKeyVaultCertificateName (requestId:Guid) paymentInstructions =

    let req = FpsPaymentsV3.BatchCreateCreditTransferRequest(
                paymentInstructions = paymentInstructions)
    let requestIdS = requestId.ToString("N") //todo, unique, save to db
    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)

    let opts = System.Text.Json.JsonSerializerOptions()
    opts.Converters.Add(ClearBank.Common.TwoDecimalsConverter())
    let client = FpsPaymentsV3.Client(httpClient, opts)

    async {

        let authToken = "Bearer " + config.PrivateKey
        let toHash = client.Serialize req
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.Post(authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            // You can use Fiddler to see the Request

            let details = getErrorDetails err
            //printfn "Used signature: %s" signature_bodyhash_string
            return Error(err, details)
    }

module MultiCurrency =

    let [<Literal>]schemaMultiCurrencyTransactions = __SOURCE_DIRECTORY__ + @"/Schemas/mccy-transactions-v1.json"
    let [<Literal>]schemaMultiCurrencyPayments = __SOURCE_DIRECTORY__ + @"/Schemas/mccy-initiate-payment-v1-dec21.json"

    type MccyTransactionsV1 = OpenApiClientProvider<schemaMultiCurrencyTransactions, PreferAsync=true>
    type MccyPaymentsV1 = OpenApiClientProvider<schemaMultiCurrencyPayments, PreferAsync=true>

    type AccountKind = GeneralSegregated | DesignatedSegregated | GeneralClient | DesignatedClient | YourFunds

    let ISOCurrencySymbols() =
        System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
        |> Array.map(fun ci -> ci.Name) |> Array.distinct |> Array.map(fun cid -> System.Globalization.RegionInfo cid)
        |> Array.map(fun g -> g.ISOCurrencySymbol) |> Array.distinct

    /// Creates a new account
    let createNewAccount config azureKeyVaultCertificateName (requestId:Guid) (sortCode:MccyTransactionsV1.RoutingCode) accountName ownerName (accountKind:AccountKind) isoCurrencySymbols identifiers productId customerId =
        let sortCode = MccyTransactionsV1.RoutingCode(sortCode.Code.Replace("-", ""), sortCode.Kind)
        let req =
            MccyTransactionsV1.CreateAccountRequest(accountName, ownerName, (accountKind.ToString()), isoCurrencySymbols, sortCode, identifiers, productId, customerId)
        let requestIdS = requestId.ToString("N") //todo, unique, save to db

        let httpClient =
            if config.LogUnsuccessfulHandler.IsNone then
                new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
            else
                let handler1 = new HttpClientHandler (UseCookies = false)
                let handler2 = new ErrorHandler(handler1)
                new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
        let client = MccyTransactionsV1.Client httpClient

        async {

            let authToken = "Bearer " + config.PrivateKey
            let toHash = client.Serialize req
            let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

            let subscription =
                if config.LogUnsuccessfulHandler.IsSome then
                    Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
                else None

            let! r = client.PostMccyV1Accounts(authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
            httpClient.Dispose()
            if subscription.IsSome then
                subscription.Value.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }

    /// Get all the accounts
    let getAccounts config =

        let httpClient =
            if config.LogUnsuccessfulHandler.IsNone then
                new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
            else
                let handler1 = new HttpClientHandler (UseCookies = false)
                let handler2 = new ErrorHandler(handler1)
                new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
        let client = MccyTransactionsV1.Client httpClient

        async {
            let authToken = "Bearer " + config.PrivateKey
            let! r = client.GetMccyV1Accounts(authToken) |> Async.Catch
            httpClient.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }

    /// Get all the transactions
    let getTransactions config accountId isoCurrecyCode pageSize pageNumber startDate endDate =

        let httpClient =
            if config.LogUnsuccessfulHandler.IsNone then
                new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
            else
                let handler1 = new HttpClientHandler (UseCookies = false)
                let handler2 = new ErrorHandler(handler1)
                new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)
        let client = MccyTransactionsV1.Client httpClient

        async {
            let authToken = "Bearer " + config.PrivateKey
            let! r = client.GetMccyV1AccountTransactions(authToken, accountId, isoCurrecyCode, pageNumber, pageSize, startDate, endDate) |> Async.Catch
            httpClient.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }


    type InternationalPaymentTransfer = {
        To: BankAccount
        AccountHolder: MccyPaymentsV1.Creditor
        Sum: decimal
        Currency: string
        Description: string
        PaymentReference: string
        TransactionId: string
    }

    ///// Creates credit transfer for createPaymentInstruction
    //let createPaymentInstruction (payment:PaymentTransfer) =
    //    MccyPaymentsV1.PaymentRequestItem(
    //        payment.TransactionId, payment.PaymentReference, Convert.ToSingle(payment.Sum),
    //        payment.AccountHolder,
    //            ), payment.AccountHolder,
    //        MccyPaymentsV1.PaymentRequestItem_DebtorAddress(),
    //        MccyPaymentsV1.AccountIdentifier(), payment.Currency)


        //    paymentIdentification = FpsPaymentsV3.BatchPaymentIdentification(
        //                                payment.Description, // instructionIdentification
        //                                payment.TransactionId // endToEndIdentification
        //                            ),

        //    amount = FpsPaymentsV3.BatchAmount(Convert.ToDouble(payment.Sum), payment.Currency),
        //    creditor = FpsPaymentsV3.BatchCreditorPartyIdentifier(payment.AccountHolder (*, "legalEntityIdentifier"*)),
        //    creditorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
        //        identification = ``account to string`` payment.To),
        //    remittanceInformation =
        //        FpsPaymentsV3.BatchRemittanceInformation(structured =
        //            FpsPaymentsV3.BatchStructured(creditorReferenceInformation =
        //                FpsPaymentsV3.BatchCreditorReferenceInformation(payment.PaymentReference // reference

        //            )
        //        )
        //    )

        /// Creates payment instructions from createCreditTransfer for transferPayments

        //let createPaymentInstruction address legalEntityIdentifier batchId account transfers =

        //let createPaymentInstruction (address:MccyPaymentsV1.PaymentRequestItem_DebtorAddress) legalEntityIdentifier batchId account transfers =

        //    let req =

        //        MccyPaymentsV1.PaymentRequestItem(
        //            payment.TransactionId, payment.PaymentReference, Convert.ToSingle(payment.Sum),
        //            payment.AccountHolder, address
        //                ), payment.AccountHolder,
        //            MccyPaymentsV1.PaymentRequestItem_DebtorAddress(),
        //            MccyPaymentsV1.AccountIdentifier(), payment.Currency)


        //        //MccyPaymentsV1.PaymentRequestItem(
        //        //    debtor = FpsPaymentsV3.BatchDebtorPartyIdentifier(
        //        //        address = address,
        //        //        legalEntityIdentifier = (legalEntityIdentifier |> Option.defaultValue "")),
        //        //    paymentInstructionIdentification = batchId,
        //        //    debtorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
        //        //        identification = ``account to string`` account
        //        //        ),
        //        //    creditTransfers = transfers
        //        //)
        //    req


    /// Post payments created with createPaymentInstruction
    let transferPayments config azureKeyVaultCertificateName (requestId:Guid) batchId isoCurrencyCode paymentInstructions =

        let req = MccyPaymentsV1.PaymentRequest(isoCurrencyCode, paymentInstructions, batchId)
            
        let requestIdS = requestId.ToString("N")
        let httpClient =
            if config.LogUnsuccessfulHandler.IsNone then
                new System.Net.Http.HttpClient(BaseAddress= Uri config.BaseUrl)
            else
                let handler1 = new HttpClientHandler (UseCookies = false)
                let handler2 = new ErrorHandler(handler1)
                new System.Net.Http.HttpClient(handler2, BaseAddress= Uri config.BaseUrl)

        let opts = System.Text.Json.JsonSerializerOptions()
        opts.Converters.Add(TwoDecimalsConverter())
        let client = MccyPaymentsV1.Client(httpClient, opts)

        async {

            let authToken = "Bearer " + config.PrivateKey
            let toHash = client.Serialize req
            let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

            let subscription =
                if config.LogUnsuccessfulHandler.IsSome then
                    Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
                else None
            let! r = client.PostV1MccyPayments(authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
            httpClient.Dispose()
            if subscription.IsSome then
                subscription.Value.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                // You can use Fiddler to see the Request

                let details = getErrorDetails err
                //printfn "Used signature: %s" signature_bodyhash_string
                return Error(err, details)
        }

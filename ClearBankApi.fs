module ClearBank

open System
open System.Net.Http
open System.Net
open SwaggerProvider

type KeyVaultCredentials =
| DefaultCredentials
| CredentialsWithOptions of Azure.Identity.DefaultAzureCredentialOptions

type ClearbankConfiguration =
    {
      BaseUrl: string
      PrivateKey: string
      AzureKeyVaultName: string
      AzureKeyVaultCredentials: KeyVaultCredentials
      LogUnsuccessfulHandler: Option<HttpStatusCode*string -> Unit>
    }

type BankAccount =
| IBAN of string
| BBAN of string
| UK_Domestic of SortCode: string * AccountNumber: string

// Dev portal: https://clearbank.github.io/uk

// Schema: https://institution-api-sim.clearbank.co.uk/docs/index.html
// FPS, CHAPS

// Account Holder Name - Required, alpha-numeric, space, comma, full-stop, hyphen (Max 18 characters)
// Sort code - 6 character length, must be numeric
// Account Number - required, 8 characters length, must be numeric
// Amount - required, must be numeric and greater than 0
// Payment Reference - required, alphanumeric, space, comma, full stop, a hyphen, maximum length 18 characters (Description to be provided -if the customer exceeds 18 characters this will be truncated)


// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]schemaV1 = __SOURCE_DIRECTORY__ + @"/clearbank-api-v1.json"
let [<Literal>]schemaV2 = __SOURCE_DIRECTORY__ + @"/clearbank-api-v2.json"
let [<Literal>]schemaV3Accounts = __SOURCE_DIRECTORY__ + @"/clearbank-api-v3-accounts.json"
let [<Literal>]schemaV3PaymentsFps = __SOURCE_DIRECTORY__ + @"/fps-initiate-payment-v3.json"

type internal ClearBankSwaggerV1 = SwaggerClientProvider<schemaV1, PreferAsync=true>
type ClearBankSwaggerV2 = SwaggerClientProvider<schemaV2, PreferAsync=true>
type ClearBankOpenApiV3Accounts = OpenApiClientProvider<schemaV3Accounts, PreferAsync=true>
type FpsPaymentsV3 = OpenApiClientProvider<schemaV3PaymentsFps, PreferAsync=true>


let unSuccessStatusCode = new Event<_>() // id, status, content
type ErrorHandler(messageHandler) =
    inherit DelegatingHandler(messageHandler)
    override __.SendAsync(request, cancellationToken) =
        let resp = base.SendAsync(request, cancellationToken)
        async {
            let! result = resp |> Async.AwaitTask
            if not result.IsSuccessStatusCode then
                let! cont = result.Content.ReadAsStringAsync() |> Async.AwaitTask
                let hasId, idvals = request.Headers.TryGetValues("X-Request-ID") // Some unique id
                unSuccessStatusCode.Trigger(
                    (if not hasId then None else idvals |> Seq.tryHead),
                    result.StatusCode,
                    cont)
            return result
        } |> Async.StartImmediateAsTask

let internal reportUnsuccessfulEvents xRequestId handler =
    let evt =
        unSuccessStatusCode.Publish
        |> Event.filter(fun (id,status,content) -> id = Some xRequestId)
        |> Event.map(fun (id,status,content) -> status, content)
    evt.Subscribe(fun (s,c) -> handler(s,c))

/// Errors
/// Possible response values:
/// Accepted, AccountDisabled, InsufficientFunds, InvalidAccount
/// InvalidCurrency, Rejected, DebitPaymentDisabled
type internal ClearBankErrorJson = FSharp.Data.JsonProvider<"""[{
    "transactions": [{
        "endToEndIdentification": "string",
        "response": "Accepted"
    }],
    "halLinks": [{
        "name": "string",
        "href": "string",
        "templated": true
    }]
},{
    "errors": {},
    "type": "string",
    "title": "string",
    "status": 3,
    "detail": "string",
    "instance": "string"
}
]""", SampleIsList=true>

type ClearBankErrorResponse = ClearBankErrorJson.Root

type ClearBankErrorStyle =
| ClearBankEmptyResponse
| ClearBankTransactionError of Errors: (string * string) seq //id and reason
| ClearBankGeneralError of Title: string * Detail: string
| ClearBankUnknownError of Content: string

let parseClearBankErrorContent(content:string) =
    if String.IsNullOrEmpty content then
        ClearBankEmptyResponse
    else

    try
        let parsed =
            ClearBankErrorJson.Parse content
        match parsed.Transactions |> Seq.tryHead with
        | Some t -> parsed.Transactions |> Seq.map(fun t -> t.EndToEndIdentification, t.Response) |> ClearBankTransactionError
        | _ ->
            if parsed.Title.IsSome && parsed.Detail.IsSome && (not (String.IsNullOrEmpty parsed.Title.Value)) then
                (parsed.Title.Value, parsed.Detail.Value) |> ClearBankGeneralError
            else
                content |> ClearBankUnknownError
    with
    | _ -> content |> ClearBankUnknownError

type ModelsV3Accounts = ClearBankOpenApiV3Accounts.ClearBank.FI.API.Accounts.Versions.V3.Models
type AccountsV3 = ModelsV3Accounts.Binding.Accounts

let internal setKeyVaultCredentials options =
    match options with
    | DefaultCredentials -> ()
    | CredentialsWithOptions opts ->
        KeyVault.configureAzureCredentials <- fun() ->
            Azure.Identity.DefaultAzureCredential opts

let internal calculateSignature config azureKeyVaultCertificateName requestBody =
    task {
        setKeyVaultCredentials config.AzureKeyVaultCredentials
        let! signature_bodyhash = KeyVault.signAsync config.AzureKeyVaultName azureKeyVaultCertificateName requestBody
        let signature_bodyhash_string =
                signature_bodyhash.Signature
                |> Convert.ToBase64String
        return signature_bodyhash_string
    }

let verifySignature publicKeyXml signature requestBody =
    task {
        let verifyResult = KeyVault.verifyPublic publicKeyXml signature requestBody
        return verifyResult
    }

let verifySignatureFromSecret config secretName signature requestBody =
    task {
        setKeyVaultCredentials config.AzureKeyVaultCredentials
        let! publicKeyXml = KeyVault.getSecretAsync config.AzureKeyVaultName secretName
        let verifyResult = KeyVault.verifyPublic publicKeyXml.Value signature requestBody
        return verifyResult
    }

let rec internal getErrorDetails : Exception -> string = function
    | :? Swagger.OpenApiException as e when e.Content <> null ->
        let content = e.Content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        content
    | :? AggregateException as aex -> getErrorDetails (aex.GetBaseException())
    | :? WebException as wex when not(isNull(wex.Response)) ->
        use stream = wex.Response.GetResponseStream()
        use reader = new System.IO.StreamReader(stream)
        let err = reader.ReadToEnd()
        err
    | :? TimeoutException as e ->
        "Timeout"
    | _ ->
        ""

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
let createPaymentInstruction address legalEntityIdentifier batchId account transfers =
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

type TwoDecimalsConverter() =
    inherit System.Text.Json.Serialization.JsonConverter<float>()

    override _.Write(writer: System.Text.Json.Utf8JsonWriter, value, serializer) =
        writer.WriteRawValue(value.ToString "0.00")

    override _.Read(reader, typeToConvert, options) =
        failwith "Not implemented"

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
    opts.Converters.Add(TwoDecimalsConverter())
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

    let [<Literal>]schemaMultiCurrencyTransactions = __SOURCE_DIRECTORY__ + @"/mccy-transactions-v1.json"
    let [<Literal>]schemaMultiCurrencyPayments = __SOURCE_DIRECTORY__ + @"/mccy-initiate-payment-v1-dec21.json"

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

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

// Schema: https://institution-api-sim.clearbank.co.uk/docs/index.html
// FPS, CHAPS

// Account Holder Name - Required, alpha-numeric, space, comma, full-stop, hyphen (Max 18 characters)
// Sort code - 6 character length, must be numeric
// Account Number - required, 8 character length, must be numeric
// Amount - required, must be numeric and greater than 0
// Payment Reference - required, alpha numeric, space, comma, full stop, hyphen, maximum length 18 characters (Description to be provided -if customer exceeds 18 characters this will be truncated)
    
let [<Literal>]schemaV1 = __SOURCE_DIRECTORY__ + @"/clearbank-api-v1.json"
let [<Literal>]schemaV2 = __SOURCE_DIRECTORY__ + @"/clearbank-api-v2.json"
type ClearBankSwaggerV1 = SwaggerClientProvider<schemaV1, PreferAsync=true>
type ClearBankSwaggerV2 = SwaggerClientProvider<schemaV2, PreferAsync=true>


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
        } |> Async.StartAsTask

let reportUnsuccessfulEvents xRequestId handler =
    let evt = 
        unSuccessStatusCode.Publish
        |> Event.filter(fun (id,status,content) -> id = Some xRequestId)
        |> Event.map(fun (id,status,content) -> status, content)
    evt.Subscribe(fun (s,c) -> handler(s,c))

/// Errors
/// Possible response values:
/// Accepted, AccountDisabled, InsufficientFunds, InvalidAccount
/// InvalidCurrency, Rejected, DebitPaymentDisabled
type ClearBankErrorJson = FSharp.Data.JsonProvider<"""[{
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

let parseClarBankErrorContent(content:string) =
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



type Models = ClearBankSwaggerV2.ClearBank.FI.API.Versions.V2.Models
type Payments = Models.Binding.Payments

let setKeyVaultCredentials options =
    match options with
    | DefaultCredentials -> ()
    | CredentialsWithOptions opts -> 
        KeyVault.configureAzureCredentials <- fun() ->
            Azure.Identity.DefaultAzureCredential opts

let calculateSignature config azureKeyVaultCertificateName requestBody =
    async {
        setKeyVaultCredentials config.AzureKeyVaultCredentials
        let! signature_bodyhash = KeyVault.signAsync config.AzureKeyVaultName azureKeyVaultCertificateName requestBody
        let signature_bodyhash_string = 
                signature_bodyhash.Signature 
                |> Convert.ToBase64String
        return signature_bodyhash_string
    }


let getErrorDetails : Exception -> string = function
    | :? WebException as wex when wex.Response <> null ->
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
            new System.Net.Http.HttpClient(BaseAddress=new Uri(config.BaseUrl))
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress=new Uri(config.BaseUrl))
    let client = ClearBankSwaggerV1.Client httpClient
    async {

        let authToken = "Bearer " + config.PrivateKey
        let payload = Newtonsoft.Json.Linq.JObject.Parse("""{"institutionId": "string","body": "hello world!"}""") |> box
        let payloaStr = Newtonsoft.Json.JsonConvert.SerializeObject payload

        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName payloaStr
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
        Payments.PaymentInstructionCounterpartAccountIdentification(iban = nr)
    | BBAN nr ->
        Payments.PaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                Models.CounterpartAccountGenericIdentification(
                    nr,
                    schemeName = Models.CounterpartAccountGenericIdentificationScheme(
                                    proprietary = "BBAN"
                                    )
                    //,"issuer"
                ))
    | UK_Domestic(sortcode, account) ->
        let identifier =
            "GBR" +
                sortcode.Replace(" ", "").Replace("-", "") +
                account.Replace(" ", "").Replace("-", "")
        Payments.PaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                Models.CounterpartAccountGenericIdentification(
                    identifier,
                    schemeName = Models.CounterpartAccountGenericIdentificationScheme(
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

let createCreditTransfer (payment:PaymentTransfer) =
    Payments.CreditTransfer(
        paymentIdentification = Payments.PaymentIdentification(
                                    payment.Description, // instructionIdentification
                                    payment.TransactionId // endToEndIdentification
                                ),

        amount = Models.Amount(Convert.ToDouble payment.Sum, payment.Currency),
        creditor = Payments.PartyIdentifier(payment.AccountHolder (*, "legalEntityIdentifier"*)),
        creditorAccount = Payments.PaymentInstructionCounterpartAccount(
            identification = ``account to string`` payment.To),
        remittanceInformation =
            Payments.RemittanceInformation(structured = 
                Payments.Structured(creditorReferenceInformation = 
                    Payments.CreditorReferenceInformation(payment.PaymentReference // reference

                )
            )
        )
    )

let createPaymentInstruction batchId account transfers =
    let req =
        Payments.PaymentInstruction(
            paymentInstructionIdentification = batchId,
            requestedExecutionDate = Some DateTime.UtcNow.Date,
            debtor = Payments.DebtorPartyIdentifier( (*legalEntityIdentifier*) ), // "string" ),
            debtorAccount = Payments.PaymentInstructionCounterpartAccount(
                identification = ``account to string`` account
                ),
            creditTransfers = transfers
            // ,"channelname", DateTime.UtcNow
        )
    req

let createNewAccount config azureKeyVaultCertificateName (requestId:Guid) sortCode accountName ownerName =
    let req =
        match ownerName with
        | Some oname -> 
            let owner = Models.Binding.Accounts.PartyIdentification(oname)
            Models.Binding.Accounts.CreateAccountRequest(accountName, sortCode, owner)
        | None -> Models.Binding.Accounts.CreateAccountRequest(accountName, sortCode.Replace("-", ""))
    let requestIdS = requestId.ToString("N") //todo, unique, save to db

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress=new Uri(config.BaseUrl))
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress=new Uri(config.BaseUrl))
    let client = ClearBankSwaggerV2.Client httpClient

    async {

        let authToken = "Bearer " + config.PrivateKey
        let toHash = client.Serialize req
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash

        let subscription = 
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.V2AccountsPost(authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

let getAccounts config =

    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress=new Uri(config.BaseUrl))
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress=new Uri(config.BaseUrl))
    let client = ClearBankSwaggerV2.Client httpClient

    async {
        let authToken = "Bearer " + config.PrivateKey
        let! r = client.V2AccountsGet(authToken) |> Async.Catch
        httpClient.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

let transferPayments config azureKeyVaultCertificateName (requestId:Guid) paymnentInstructions =

    let req = Payments.CreateCreditTransferRequest(
                paymentInstructions = paymnentInstructions)
    let requestIdS = requestId.ToString("N") //todo, unique, save to db
    let httpClient =
        if config.LogUnsuccessfulHandler.IsNone then
            new System.Net.Http.HttpClient(BaseAddress=new Uri(config.BaseUrl))
        else
            let handler1 = new HttpClientHandler (UseCookies = false)
            let handler2 = new ErrorHandler(handler1)
            new System.Net.Http.HttpClient(handler2, BaseAddress=new Uri(config.BaseUrl))

    let client = ClearBankSwaggerV2.Client httpClient

    async {

        let authToken = "Bearer " + config.PrivateKey
        let scheme = "FPS" // "Bacs"
        let toHash = client.Serialize req
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash
            
        let subscription = 
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.V2PaymentsBySchemePost(scheme, authToken, signature_bodyhash_string, requestIdS, req) |> Async.Catch
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

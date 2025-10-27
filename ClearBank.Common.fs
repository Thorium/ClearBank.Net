module ClearBank.Common

open System
open System.Net.Http
open System.Net

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
    | :? Swagger.OpenApiException as e when not(isNull e.Content) ->
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

type internal TwoDecimalsConverter() =
    inherit System.Text.Json.Serialization.JsonConverter<float>()

    override _.Write(writer: System.Text.Json.Utf8JsonWriter, value, serializer) =
        writer.WriteRawValue(value.ToString "0.00")

    override _.Read(reader, typeToConvert, options) =
        failwith "Not implemented"

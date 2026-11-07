module ClearBank.Common

open System
open System.Net.Http
open System.Net
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks

type KeyVaultCredentials =
| DefaultCredentials
| CredentialsWithOptions of Azure.Identity.DefaultAzureCredentialOptions

/// PrivateKey is the API token. Printing a configuration (%A, ToString) hides it.
[<StructuredFormatDisplay("{Display}")>]
type ClearbankConfiguration =
    {
      BaseUrl: string
      PrivateKey: string
      AzureKeyVaultName: string
      AzureKeyVaultCredentials: KeyVaultCredentials
      LogUnsuccessfulHandler: Option<HttpStatusCode*string -> Unit>
    }

    member this.Display = $"{{ BaseUrl = {this.BaseUrl}; AzureKeyVaultName = {this.AzureKeyVaultName}; PrivateKey hidden }}"
    override this.ToString() = this.Display

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

/// id, status, content
let unSuccessStatusCode = Event<_>()
type ErrorHandler(messageHandler) =
    inherit DelegatingHandler(messageHandler)
    override __.SendAsync(request, cancellationToken) =
        let resp = base.SendAsync(request, cancellationToken)
        async {
            let! result = resp |> Async.AwaitTask
            if not result.IsSuccessStatusCode then
                let! cont = result.Content.ReadAsStringAsync() |> Async.AwaitTask
                let hasId, idvals = request.Headers.TryGetValues "X-Request-ID" // Some unique id
                unSuccessStatusCode.Trigger(
                    (if not hasId then None else idvals |> Seq.tryHead),
                    result.StatusCode,
                    cont)
            return result
        } |> Async.StartImmediateAsTask

let internal reportUnsuccessfulEvents xRequestId handler =
    let evt =
        unSuccessStatusCode.Publish
        |> Event.filter(fun (pid,status,content) -> pid = Some xRequestId)
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
///id and reason
| ClearBankTransactionError of Errors: (string * string) seq
| ClearBankGeneralError of Title: string * Detail: string
| ClearBankUnknownError of Content: string

let parseClearBankErrorContent(content:string) =
    if String.IsNullOrEmpty content then
        ClearBankEmptyResponse
    else

    try
        let parsed =
            ClearBankErrorJson.Parse content
        match parsed.Transactions |> Array.tryHead with
        | Some t -> parsed.Transactions |> Seq.map(fun t -> t.EndToEndIdentification, t.Response) |> ClearBankTransactionError
        | None ->
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


/// Signs a request body for the DigitalSignature header.
/// ClearBank expects RSASSA-PKCS1-v1_5 with SHA-256 over the UTF-8 bytes of the exact body sent.
[<NoEquality; NoComparison>]
type RequestSigner =
    { Sign: string -> CancellationToken -> Task<byte[]> }

module RequestSigner =

    let private sha256 (body: string) =
        use sha = SHA256.Create()
        sha.ComputeHash(Text.Encoding.UTF8.GetBytes body)

    /// Signs with a key kept in Azure Key Vault (follows KeyVault.configureAlgorithm and KeyVault.configureEncoding).
    let azureKeyVault (credentials: KeyVaultCredentials) (vaultName: string) (keyName: string) =
        { Sign =
            fun body _ ->
                task {
                    setKeyVaultCredentials credentials
                    let! result = KeyVault.signAsync vaultName keyName body
                    return result.Signature
                } }

    /// Signs with the Azure Key Vault named in the configuration.
    let fromConfig (config: ClearbankConfiguration) (azureKeyVaultCertificateName: string) =
        azureKeyVault config.AzureKeyVaultCredentials config.AzureKeyVaultName azureKeyVaultCertificateName

    /// Signs with a local RSA private key.
    let rsa (key: RSA) =
        { Sign =
            fun body _ ->
                key.SignData(
                    Text.Encoding.UTF8.GetBytes body,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1
                )
                |> Task.FromResult }

    /// Signs through a remote key that signs a SHA-256 digest, e.g. AWS KMS Sign with
    /// MessageType = DIGEST and SigningAlgorithm = RSASSA_PKCS1_V1_5_SHA_256.
    let ofDigest (signDigest: byte[] -> CancellationToken -> Task<byte[]>) =
        { Sign = fun body cancellationToken -> signDigest (sha256 body) cancellationToken }

    /// A signer for clients that only read: signing fails.
    let internal unavailable =
        { Sign = fun _ _ -> Task.FromException<byte[]>(InvalidOperationException "This call has no signing key configured.") }

type RequestSigner with

    /// A signer from a function of (body, cancellation token) to the signature bytes.
    static member Create(sign: Func<string, CancellationToken, Task<byte[]>>) =
        { Sign = fun body cancellationToken -> sign.Invoke(body, cancellationToken) }

    /// A signer from a function of (SHA-256 digest, cancellation token) to the signature bytes, such as AWS KMS.
    static member FromDigest(signDigest: Func<byte[], CancellationToken, Task<byte[]>>) =
        RequestSigner.ofDigest (fun digest cancellationToken -> signDigest.Invoke(digest, cancellationToken))

    /// A signer over a local RSA private key.
    static member FromRsa(key: RSA) = RequestSigner.rsa key

    /// A signer over an Azure Key Vault key.
    static member AzureKeyVault(credentials, vaultName, keyName) = RequestSigner.azureKeyVault credentials vaultName keyName

let internal calculateSignature config azureKeyVaultCertificateName requestBody =
    task {
        let! signature = (RequestSigner.fromConfig config azureKeyVaultCertificateName).Sign requestBody CancellationToken.None
        return Convert.ToBase64String signature
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

/// A non-success response from an endpoint the bundled schemas don't describe.
[<Sealed>]
type ClearBankResponseException(status: HttpStatusCode, content: string) =
    inherit HttpRequestException($"Response status code does not indicate success: {int status} ({status}).")
    member _.Status = status
    member _.Content = content

let rec internal getErrorDetails : Exception -> string = function
    | :? Swagger.OpenApiException as e when not(isNull e.Content) ->
        let content = e.Content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        content
    | :? ClearBankResponseException as e -> e.Content
    | :? AggregateException as aex -> getErrorDetails (aex.GetBaseException())
    | :? WebException as wex when not(isNull wex.Response) ->
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
        writer.WriteRawValue(value.ToString("0.00", Globalization.CultureInfo.InvariantCulture))

    override _.Read(reader, typeToConvert, options) = reader.GetDouble()

/// Reports every non-success response to a callback.
type internal LoggingHandler(inner: HttpMessageHandler, log: HttpStatusCode * string -> unit) =
    inherit DelegatingHandler(inner)

    override _.SendAsync(request, cancellationToken) =
        let response = base.SendAsync(request, cancellationToken)
        task {
            let! result = response
            if not result.IsSuccessStatusCode then
                let! content = result.Content.ReadAsStringAsync()
                log (result.StatusCode, content)
            return result
        }

/// A ClearBank API connection: one HttpClient shared by every call (its BaseAddress is the API root),
/// the API token sent as "Authorization: Bearer", and the signer for the DigitalSignature header.
/// Printing a client (%A, ToString) hides the token.
[<NoEquality; NoComparison; StructuredFormatDisplay("{Display}")>]
type ClearBankClient =
    { Http: HttpClient
      ApiToken: string
      Signer: RequestSigner }

    member internal this.Authorization = "Bearer " + this.ApiToken
    member this.Display = "{ ApiToken hidden }"
    override this.ToString() = this.Display

module ClearBankClient =

    /// A client over a caller-owned HttpClient whose BaseAddress is the ClearBank API root.
    let create (http: HttpClient) (apiToken: string) (signer: RequestSigner) =
        if isNull http.BaseAddress then
            invalidArg (nameof http) "The HttpClient needs a BaseAddress, e.g. https://institution-api-sim.clearbank.co.uk/"

        { Http = http; ApiToken = apiToken; Signer = signer }

    /// An HttpClient for the ClearBank API root, optionally reporting non-success responses. It doesn't follow redirects, which would send
    /// signatures and payment bodies to wherever one points. Keep it for the application's lifetime.
    let createHttpClient (baseUrl: string) (logUnsuccessful: (HttpStatusCode * string -> unit) option) =
#if NETSTANDARD
        let transport = new HttpClientHandler(UseCookies = false, AllowAutoRedirect = false) :> HttpMessageHandler
#else
        let transport = new SocketsHttpHandler(UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes 5., AllowAutoRedirect = false) :> HttpMessageHandler
#endif
        let handler =
            match logUnsuccessful with
            | Some log -> new LoggingHandler(transport, log) :> HttpMessageHandler
            | None -> transport

        new HttpClient(handler, BaseAddress = Uri baseUrl)

    /// A long-lived client from the configuration: a new HttpClient with its LogUnsuccessfulHandler, and the given signer.
    let fromConfig (config: ClearbankConfiguration) (signer: RequestSigner) =
        create (createHttpClient config.BaseUrl config.LogUnsuccessfulHandler) config.PrivateKey signer

type ClearBankClient with

    /// A client over a caller-owned HttpClient whose BaseAddress is the ClearBank API root.
    static member Create(http, apiToken, signer) = ClearBankClient.create http apiToken signer

/// Runs a call; failures become Error (exception, response body). Cancellation of the token propagates.
let internal attempt (cancellationToken: CancellationToken) (call: Async<'T>) : Task<Result<'T, exn * string>> =
    task {
        try
            let! value = Async.StartAsTask(call, cancellationToken = cancellationToken)
            return Ok value
        with
        | err when not (cancellationToken.IsCancellationRequested && (err :? OperationCanceledException)) ->
            return Error(err, getErrorDetails err)
    }

/// Serializes and signs the body, then runs the call with the base64 signature; a serialization failure is an Error too.
let internal signed (client: ClearBankClient) (serialize: unit -> string) cancellationToken (call: string -> Async<'T>) =
    attempt cancellationToken (async {
        let! signature = client.Signer.Sign (serialize ()) cancellationToken |> Async.AwaitTask
        return! call (Convert.ToBase64String signature)
    })

let internal requestIdText (requestId: Guid) = requestId.ToString "N"
let internal idText (id: Guid) = id.ToString "D"

/// A DateTime as an instant; an unspecified kind counts as UTC.
let internal asOffset (value: DateTime) =
    match value.Kind with
    | DateTimeKind.Unspecified -> DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    | _ -> DateTimeOffset value

/// Sends to a path the bundled schemas don't describe. A body is signed; a non-success status
/// becomes a ClearBankResponseException carrying the response body.
let internal sendJson (client: ClearBankClient) (httpMethod: HttpMethod) (path: string) (requestId: Guid) (body: string voption) cancellationToken =
    attempt cancellationToken (async {
        use request = new HttpRequestMessage(httpMethod, path)
        request.Headers.TryAddWithoutValidation("Authorization", client.Authorization) |> ignore
        request.Headers.Add("X-Request-Id", requestIdText requestId)

        match body with
        | ValueSome json ->
            let! signature = client.Signer.Sign json cancellationToken |> Async.AwaitTask
            request.Headers.Add("DigitalSignature", Convert.ToBase64String signature)
            request.Content <- new StringContent(json, Text.Encoding.UTF8, "application/json")
        | ValueNone -> ()

        use! response = client.Http.SendAsync(request, cancellationToken) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask

        return
            if response.IsSuccessStatusCode then content
            else raise (ClearBankResponseException(response.StatusCode, content))
    })

/// Runs one call of the configuration-based API: a per-call HttpClient, the Azure Key Vault signer,
/// and LogUnsuccessfulHandler for this request id. The caller's cancelled token cancels the Async, which AwaitTask alone would report
/// as a failure, so Async.StartAsTask ends Canceled. A timeout is not the caller's cancellation and stays an Error.
let internal perCall (config: ClearbankConfiguration) (signer: RequestSigner) (requestId: Guid option) (call: ClearBankClient -> CancellationToken -> Task<Result<'T, exn * string>>) =
    async {
        use http =
            match config.LogUnsuccessfulHandler with
            | None -> new HttpClient(new HttpClientHandler(UseCookies = false, AllowAutoRedirect = false), BaseAddress = Uri config.BaseUrl)
            | Some _ -> new HttpClient(new ErrorHandler(new HttpClientHandler(UseCookies = false, AllowAutoRedirect = false)), BaseAddress = Uri config.BaseUrl)

        use _subscription =
            match requestId, config.LogUnsuccessfulHandler with
            | Some pid, Some handler -> reportUnsuccessfulEvents (requestIdText pid) handler
            | _ -> { new IDisposable with member _.Dispose() = () }

        let! cancellationToken = Async.CancellationToken
        try
            return! call { Http = http; ApiToken = config.PrivateKey; Signer = signer } cancellationToken |> Async.AwaitTask
        with :? OperationCanceledException as cancelled when cancellationToken.IsCancellationRequested ->
            return! Async.FromContinuations(fun (_, _, cancel) -> cancel cancelled)
    }

/// Parses a successful response body; a parse failure becomes Error (exception, body).
let internal parseWith (parse: string -> 'T) (response: Task<Result<string, exn * string>>) =
    task {
        match! response with
        | Ok content ->
            try
                return Ok(parse content)
            with err ->
                return Error(err, content)
        | Error failure -> return Error failure
    }

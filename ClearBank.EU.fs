module ClearBank.EU

open System
open System.Net.Http
open System.Net
open SwaggerProvider
open ClearBank.Common


//// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]sepaV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-ct-v1.json"
let [<Literal>]sepaInstantV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-instant-v1.json"
//let [<Literal>]t2V1 = __SOURCE_DIRECTORY__ + @"/Schemas/t2-v1.json"

type SepaV1 = OpenApiClientProvider<sepaV1, PreferAsync=true>
type SepaInstantV1 = OpenApiClientProvider<sepaInstantV1, PreferAsync=true>
//type T2V1 = OpenApiClientProvider<t2V1, PreferAsync=true>

/// Post SEPA payments created with SepaV1.CreateSepaOutboundPaymentRequest(...)
let sepaTransferPayments config azureKeyVaultCertificateName (requestId:Guid) sepaOutboundPaymentRequest =

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
    let client = SepaV1.Client(httpClient, opts)

    async {
        let authToken = "Bearer " + config.PrivateKey
        let toHash = client.Serialize sepaOutboundPaymentRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentCreate(authToken, signature_bodyhash_string, requestIdS, sepaOutboundPaymentRequest) |> Async.Catch
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

/// Post SEPA instant payments created with SepaInstantV1.CreateSepaOutboundPaymentRequest(...)
let sepaInstantTransferPayments config azureKeyVaultCertificateName (requestId:Guid) sepaOutboundPaymentRequest =
    
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
    let client = SepaInstantV1.Client(httpClient, opts)

    async {
        let authToken = "Bearer " + config.PrivateKey
        let toHash = client.Serialize sepaOutboundPaymentRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentCreate(authToken, signature_bodyhash_string, requestIdS, sepaOutboundPaymentRequest) |> Async.Catch
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

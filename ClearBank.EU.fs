module ClearBank.EU

open System
open System.Net.Http
open System.Net
open SwaggerProvider
open ClearBank.Common


//// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]sepaV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-ct-v1.json"
let [<Literal>]sepaInstantV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-instant-v1.json"
let [<Literal>]t2V1 = __SOURCE_DIRECTORY__ + @"/Schemas/t2-v1.json"

type SepaV1 = OpenApiClientProvider<sepaV1, PreferAsync=true>
type SepaInstantV1 = OpenApiClientProvider<sepaInstantV1, PreferAsync=true>
type T2V1 = OpenApiClientProvider<t2V1, PreferAsync=true>

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

/// Post SEPA CT recall response created with SepaV1.RecallOfInboundPaymentReviewRequest(...)
let sepaRecallResponse config azureKeyVaultCertificateName (requestId:Guid) recallResponseRequest =
    
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
        let toHash = client.Serialize recallResponseRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentsInboundRecallRequestResult(authToken, signature_bodyhash_string, requestIdS, recallResponseRequest) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Post SEPA CT payment recall request created with SepaV1.RecallOfOutboundPaymentRequest(...)
let sepaRecallRequest config azureKeyVaultCertificateName (requestId:Guid) recallRequest =
    
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
        let toHash = client.Serialize recallRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentRecall(authToken, signature_bodyhash_string, requestIdS, recallRequest) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Post SEPA CT payment return created with SepaV1.ReturnOfInboundPaymentRequest(...)
let sepaPaymentReturn config azureKeyVaultCertificateName (requestId:Guid) paymentReturnRequest =
    
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
        let toHash = client.Serialize paymentReturnRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentsInboundReturnOfInboundPayment(authToken, signature_bodyhash_string, requestIdS, paymentReturnRequest) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Post SEPA Instant recall response created with SepaInstantV1.RecallResponseReceived(...)
let sepaInstantRecallResponse config azureKeyVaultCertificateName (requestId:Guid) (paymentId:Guid) recallResponseRequest =
    
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
        let toHash = client.Serialize recallResponseRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.SepaInstantRecallResponse(paymentId, authToken, signature_bodyhash_string, requestIdS, recallResponseRequest) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

/// Post SEPA Instant recall request created with SepaInstantV1.CreateRequestForRecallRequest(...)
let sepaInstantRecallRequest config azureKeyVaultCertificateName (requestId:Guid) (paymentId:Guid) recallRequest =
    
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
        let toHash = client.Serialize recallRequest
        let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

        let subscription =
            if config.LogUnsuccessfulHandler.IsSome then
                Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
            else None
        let! r = client.PaymentRequestForRecall(paymentId, authToken, signature_bodyhash_string, requestIdS, recallRequest) |> Async.Catch
        httpClient.Dispose()
        if subscription.IsSome then
            subscription.Value.Dispose()
        match r with
        | Choice1Of2 x -> return Ok x
        | Choice2Of2 err ->
            let details = getErrorDetails err
            return Error(err, details)
    }

module Target2 =
    
    /// Post T2-RTGS institution payment created with T2V1.InstitutionPayments.InstitutionPaymentRequest(...)
    let institutionPayment config azureKeyVaultCertificateName (requestId:Guid) institutionPaymentRequest =
        
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
        let client = T2V1.Client(httpClient, opts)

        async {
            let authToken = "Bearer " + config.PrivateKey
            let toHash = client.Serialize institutionPaymentRequest
            let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

            let subscription =
                if config.LogUnsuccessfulHandler.IsSome then
                    Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
                else None
            let! r = client.PostPaymentsT2RtgsV1InstitutionPayments(authToken, signature_bodyhash_string, requestIdS, institutionPaymentRequest) |> Async.Catch
            httpClient.Dispose()
            if subscription.IsSome then
                subscription.Value.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }

    /// Post T2-RTGS customer payment created with T2V1.CustomerPayments.CustomerPaymentRequest(...)
    let customerPayment config azureKeyVaultCertificateName (requestId:Guid) customerPaymentRequest =
        
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
        let client = T2V1.Client(httpClient, opts)

        async {
            let authToken = "Bearer " + config.PrivateKey
            let toHash = client.Serialize customerPaymentRequest
            let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

            let subscription =
                if config.LogUnsuccessfulHandler.IsSome then
                    Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
                else None
            let! r = client.PostPaymentsT2RtgsV1CustomerPayments(authToken, signature_bodyhash_string, requestIdS, customerPaymentRequest) |> Async.Catch
            httpClient.Dispose()
            if subscription.IsSome then
                subscription.Value.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }

    /// Post T2-RTGS payment return created with T2V1.PaymentReturns.PaymentReturnRequest(...)
    let paymentReturn config azureKeyVaultCertificateName (requestId:Guid) paymentReturnRequest =
        
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
        let client = T2V1.Client(httpClient, opts)

        async {
            let authToken = "Bearer " + config.PrivateKey
            let toHash = client.Serialize paymentReturnRequest
            let! signature_bodyhash_string = calculateSignature config azureKeyVaultCertificateName toHash |> Async.AwaitTask

            let subscription =
                if config.LogUnsuccessfulHandler.IsSome then
                    Some (reportUnsuccessfulEvents requestIdS config.LogUnsuccessfulHandler.Value)
                else None
            let! r = client.PostPaymentsT2RtgsV1PaymentReturns(authToken, signature_bodyhash_string, requestIdS, paymentReturnRequest) |> Async.Catch
            httpClient.Dispose()
            if subscription.IsSome then
                subscription.Value.Dispose()
            match r with
            | Choice1Of2 x -> return Ok x
            | Choice2Of2 err ->
                let details = getErrorDetails err
                return Error(err, details)
        }

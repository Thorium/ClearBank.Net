module ClearBank.EU

open System
open System.Text.Json
open SwaggerProvider
open ClearBank.Common


//// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]sepaV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-ct-v1.json"
let [<Literal>]sepaInstantV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sepa-instant-v1.json"
let [<Literal>]t2V1 = __SOURCE_DIRECTORY__ + @"/Schemas/t2-v1.json"

type SepaV1 = OpenApiClientProvider<sepaV1, PreferAsync=true>
type SepaInstantV1 = OpenApiClientProvider<sepaInstantV1, PreferAsync=true>
type T2V1 = OpenApiClientProvider<t2V1, PreferAsync=true>

/// Task-based SEPA and Target2 operations over a shared ClearBankClient. Failures are Error (exception, response body).
/// Amounts are sent with two decimals; one that two decimals don't hold exactly is an Error and nothing is sent.
module Api =

    let private paymentOptions =
        let options = JsonSerializerOptions()
        options.Converters.Add(TwoDecimalsConverter())
        options

    let private sepa (client: ClearBankClient) = SepaV1.Client(client.CheckedHttp, paymentOptions)
    let private sepaInstant (client: ClearBankClient) = SepaInstantV1.Client(client.CheckedHttp, paymentOptions)
    let private t2 (client: ClearBankClient) = T2V1.Client(client.CheckedHttp, paymentOptions)

    /// Posts a SEPA payment created with SepaV1.CreateSepaOutboundPaymentRequest(...)
    let sepaTransferPaymentsAsync (client: ClearBankClient) (requestId: Guid) sepaOutboundPaymentRequest cancellationToken =
        let api = sepa client
        signed client (fun () -> api.Serialize sepaOutboundPaymentRequest) cancellationToken (fun signature ->
            api.PaymentCreate(client.Authorization, signature, requestIdText requestId, sepaOutboundPaymentRequest, cancellationToken))

    /// Posts a SEPA Instant payment created with SepaInstantV1.CreateSepaOutboundPaymentRequest(...)
    let sepaInstantTransferPaymentsAsync (client: ClearBankClient) (requestId: Guid) sepaOutboundPaymentRequest cancellationToken =
        let api = sepaInstant client
        signed client (fun () -> api.Serialize sepaOutboundPaymentRequest) cancellationToken (fun signature ->
            api.PaymentCreate(client.Authorization, signature, requestIdText requestId, sepaOutboundPaymentRequest, cancellationToken))

    /// Posts a SEPA CT recall response created with SepaV1.RecallOfInboundPaymentReviewRequest(...)
    let sepaRecallResponseAsync (client: ClearBankClient) (requestId: Guid) recallResponseRequest cancellationToken =
        let api = sepa client
        signed client (fun () -> api.Serialize recallResponseRequest) cancellationToken (fun signature ->
            api.PaymentsInboundRecallRequestResult(client.Authorization, signature, requestIdText requestId, recallResponseRequest, cancellationToken))

    /// Posts a SEPA CT payment recall request created with SepaV1.RecallOfOutboundPaymentRequest(...)
    let sepaRecallRequestAsync (client: ClearBankClient) (requestId: Guid) recallRequest cancellationToken =
        let api = sepa client
        signed client (fun () -> api.Serialize recallRequest) cancellationToken (fun signature ->
            api.PaymentRecall(client.Authorization, signature, requestIdText requestId, recallRequest, cancellationToken))

    /// Posts a SEPA CT payment return created with SepaV1.ReturnOfInboundPaymentRequest(...)
    let sepaPaymentReturnAsync (client: ClearBankClient) (requestId: Guid) paymentReturnRequest cancellationToken =
        let api = sepa client
        signed client (fun () -> api.Serialize paymentReturnRequest) cancellationToken (fun signature ->
            api.PaymentsInboundReturnOfInboundPayment(client.Authorization, signature, requestIdText requestId, paymentReturnRequest, cancellationToken))

    /// Posts a SEPA Instant recall response created with SepaInstantV1.RecallResponseReceived(...)
    let sepaInstantRecallResponseAsync (client: ClearBankClient) (requestId: Guid) (paymentId: Guid) recallResponseRequest cancellationToken =
        let api = sepaInstant client
        signed client (fun () -> api.Serialize recallResponseRequest) cancellationToken (fun signature ->
            api.SepaInstantRecallResponse(paymentId, client.Authorization, signature, requestIdText requestId, recallResponseRequest, cancellationToken))

    /// Posts a SEPA Instant recall request created with SepaInstantV1.CreateRequestForRecallRequest(...)
    let sepaInstantRecallRequestAsync (client: ClearBankClient) (requestId: Guid) (paymentId: Guid) recallRequest cancellationToken =
        let api = sepaInstant client
        signed client (fun () -> api.Serialize recallRequest) cancellationToken (fun signature ->
            api.PaymentRequestForRecall(paymentId, client.Authorization, signature, requestIdText requestId, recallRequest, cancellationToken))

    /// Posts a T2-RTGS institution payment created with T2V1.InstitutionPayments.InstitutionPaymentRequest(...)
    let target2InstitutionPaymentAsync (client: ClearBankClient) (requestId: Guid) institutionPaymentRequest cancellationToken =
        let api = t2 client
        signed client (fun () -> api.Serialize institutionPaymentRequest) cancellationToken (fun signature ->
            api.PostPaymentsT2RtgsV1InstitutionPayments(client.Authorization, signature, requestIdText requestId, institutionPaymentRequest, cancellationToken))

    /// Posts a T2-RTGS customer payment created with T2V1.CustomerPayments.CustomerPaymentRequest(...)
    let target2CustomerPaymentAsync (client: ClearBankClient) (requestId: Guid) customerPaymentRequest cancellationToken =
        let api = t2 client
        signed client (fun () -> api.Serialize customerPaymentRequest) cancellationToken (fun signature ->
            api.PostPaymentsT2RtgsV1CustomerPayments(client.Authorization, signature, requestIdText requestId, customerPaymentRequest, cancellationToken))

    /// Posts a T2-RTGS payment return created with T2V1.PaymentReturns.PaymentReturnRequest(...)
    let target2PaymentReturnAsync (client: ClearBankClient) (requestId: Guid) paymentReturnRequest cancellationToken =
        let api = t2 client
        signed client (fun () -> api.Serialize paymentReturnRequest) cancellationToken (fun signature ->
            api.PostPaymentsT2RtgsV1PaymentReturns(client.Authorization, signature, requestIdText requestId, paymentReturnRequest, cancellationToken))

let private signedCall config azureKeyVaultCertificateName (requestId: Guid) call =
    perCall config (RequestSigner.fromConfig config azureKeyVaultCertificateName) (Some requestId) call

/// Post SEPA payments created with SepaV1.CreateSepaOutboundPaymentRequest(...)
let sepaTransferPayments config azureKeyVaultCertificateName (requestId:Guid) sepaOutboundPaymentRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaTransferPaymentsAsync client requestId sepaOutboundPaymentRequest ct)

/// Post SEPA instant payments created with SepaInstantV1.CreateSepaOutboundPaymentRequest(...)
let sepaInstantTransferPayments config azureKeyVaultCertificateName (requestId:Guid) sepaOutboundPaymentRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaInstantTransferPaymentsAsync client requestId sepaOutboundPaymentRequest ct)

/// Post SEPA CT recall response created with SepaV1.RecallOfInboundPaymentReviewRequest(...)
let sepaRecallResponse config azureKeyVaultCertificateName (requestId:Guid) recallResponseRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaRecallResponseAsync client requestId recallResponseRequest ct)

/// Post SEPA CT payment recall request created with SepaV1.RecallOfOutboundPaymentRequest(...)
let sepaRecallRequest config azureKeyVaultCertificateName (requestId:Guid) recallRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaRecallRequestAsync client requestId recallRequest ct)

/// Post SEPA CT payment return created with SepaV1.ReturnOfInboundPaymentRequest(...)
let sepaPaymentReturn config azureKeyVaultCertificateName (requestId:Guid) paymentReturnRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaPaymentReturnAsync client requestId paymentReturnRequest ct)

/// Post SEPA Instant recall response created with SepaInstantV1.RecallResponseReceived(...)
let sepaInstantRecallResponse config azureKeyVaultCertificateName (requestId:Guid) (paymentId:Guid) recallResponseRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaInstantRecallResponseAsync client requestId paymentId recallResponseRequest ct)

/// Post SEPA Instant recall request created with SepaInstantV1.CreateRequestForRecallRequest(...)
let sepaInstantRecallRequest config azureKeyVaultCertificateName (requestId:Guid) (paymentId:Guid) recallRequest =
    signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
        Api.sepaInstantRecallRequestAsync client requestId paymentId recallRequest ct)

module Target2 =

    /// Post T2-RTGS institution payment created with T2V1.InstitutionPayments.InstitutionPaymentRequest(...)
    let institutionPayment config azureKeyVaultCertificateName (requestId:Guid) institutionPaymentRequest =
        signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
            Api.target2InstitutionPaymentAsync client requestId institutionPaymentRequest ct)

    /// Post T2-RTGS customer payment created with T2V1.CustomerPayments.CustomerPaymentRequest(...)
    let customerPayment config azureKeyVaultCertificateName (requestId:Guid) customerPaymentRequest =
        signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
            Api.target2CustomerPaymentAsync client requestId customerPaymentRequest ct)

    /// Post T2-RTGS payment return created with T2V1.PaymentReturns.PaymentReturnRequest(...)
    let paymentReturn config azureKeyVaultCertificateName (requestId:Guid) paymentReturnRequest =
        signedCall config azureKeyVaultCertificateName requestId (fun client ct ->
            Api.target2PaymentReturnAsync client requestId paymentReturnRequest ct)

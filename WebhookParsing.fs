module ClearBank.Webhooks
open System
open System.Net.Http
open FSharp.Data.JsonProvider

[<Sealed>]
type WebHookResponse (nonce:int64) =
    member val Nonce = nonce |> Convert.ToInt32

let internal cbSerializerSettings = Newtonsoft.Json.JsonSerializerSettings(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)

/// Creates a plain text response
let createResponsePlain (nonce:int64) =
    Newtonsoft.Json.JsonConvert.SerializeObject(WebHookResponse nonce, cbSerializerSettings)

/// Creates a valid response, signed with the given signer, that will not escape JSON object strings
let createSignedResponseAsync (signer: ClearBank.Common.RequestSigner) (request:HttpRequestMessage) nonce cancellationToken =
    task {
        let responseContent = nonce |> createResponsePlain

        let! signature = signer.Sign responseContent cancellationToken

        // The purpose of StringContent over CreateResponse() is to avoid string escaping.
        let response =
            new HttpResponseMessage(
                System.Net.HttpStatusCode.OK,
                RequestMessage = request,
                Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json"))

        response.Headers.Add("DigitalSignature", Convert.ToBase64String signature)
        return response
    }

/// Creates a valid response that will not escape JSON object strings
let createResponse config azureKeyVaultCertificateName (request:HttpRequestMessage) nonce =
    createSignedResponseAsync (ClearBank.Common.RequestSigner.fromConfig config azureKeyVaultCertificateName) request nonce System.Threading.CancellationToken.None

module UK =
    type internal ClearBankPaymentJson = FSharp.Data.JsonProvider<"""[
{
    "Type": "TransactionSettled",
    "Version": 6,
    "Payload": {},
    "Nonce": 123456789012
},
{
    "Type": "TransactionSettled",
    "Version": 6,
    "Payload": {
        "TransactionId": "d9162680-7d35-7994-4e38-fa80716899a2",
        "Status": "Settled",
        "Scheme": "Transfer",
        "EndToEndTransactionId": "05726acd06dc",
        "Amount": 88.52,
        "TimestampSettled": "2019-01-01T10:02:00.964Z",
        "TimestampCreated": "2019-01-01T11:12:05.101Z",
        "CurrencyCode": "GBP",
        "DebitCreditCode": "Credit",
        "Reference": "51dcf7ba480e61c5a60bbb6c6b774d17string",
        "IsReturn": false,
        "Account": {
            "IBAN": "GB00CUBK11223312345678",
            "BBAN": "CUBK11223312345678",
            "OwnerName": "John Doe",
            "TransactionOwnerName": "John Doe",
            "InstitutionName": "CustomerBank"
        },
        "CounterpartAccount": {
            "IBAN": "GB00CUBK44556687654321",
            "BBAN": "CUBK44556687654321",
            "OwnerName": "Jane Doe",
            "TransactionOwnerName": "Jane Doe",
            "InstitutionName": "CustomerBank"
        },
        "ActualEndToEndTransactionId": "e6e7ab2a97d1",
        "DirectDebitMandateId": "cbf17eb8-9e49-2043-2eca-0dae05e27fe5",
        "TransactionCode": "99",
        "ServiceUserNumber": "030201",
        "BacsTransactionId": "3ec6f5b0-0e6b-a30b-7dc4-cdcc0751d448",
        "BacsTransactionDescription": "CreditContraTransactionSettled",
        "TransactionType": "DirectCredit",
        "TransactionSource": "CardProcessor",
        "SupplementaryData": [
            {
                "Name": "20",
                "Value": "AJF9384DGSB48Sd"
            },
            {
                "Name": "23B",
                "Value": "CRED"
            }
        ]
    },
    "Nonce": 949893874
},
{
    "Type": "PaymentMessageAssessmentFailed",
    "Version": 1,
    "Payload": {
        "MessageId": "624e0276-1b27-4a80-a06d-51759711bc0e",
        "PaymentMethodType": "Transfer",
        "AssesmentFailure": [
            {
                "EndToEndId": "51236da640c9",
                "Reasons": [
                    "Insufficient Funds"
                ]
            },
            {
                "EndToEndId": "fa2b005bf7a4",
                "Reasons": [
                    "Account closed"
                ]
            }
        ],
        "AccountIdentification": {
            "Debtor": {
                "IBAN": "GB00CUBK22002243218765",
                "BBAN": "CUBK22002243218765"
            },
            "Creditors": [
                {
                    "Reference": "b4e062e244c449f8afc7e2a941562768",
                    "Amount": 17.95,
                    "CurrencyCode": "GBP",
                    "IBAN": "GB00CUBK44556687654321",
                    "BBAN": "CUBK44556687654321"
                },
                {
                    "Reference": "0bae808d96bb42a6a892bc9ade169993",
                    "Amount": 17.95,
                    "CurrencyCode": "GBP",
                    "IBAN": "GB00CUBK11223312345678",
                    "BBAN": "CUBK11223312345678"
                }
            ]
        }
    },
    "Nonce": 1082937278
},
{
    "Type": "PaymentMessageValidationFailed",
    "Version": 1,
    "Payload": {
        "MessageId": "164ac6a0-8e15-4592-b99e-c0a7399f1295",
        "PaymentMethodType": "Transfer",
        "ValidationFailure": [
            {
                "EndToEndId": "72db39cd1693",
                "Reasons": [
                    "Creditor name contains invalid characters"
                ]
            }
        ],
        "AccountIdentification": {
            "Debtor": {
                "IBAN": "GB00CUBK22002243218765",
                "BBAN": "CUBK22002243218765",
                "CUID": null,
                "UPIC": null,
                "AccountName": "Jane Doe",
                "AccountHolderLabel": "Jane Doe",
                "InstitutionName": "CustomerBank"
            },
            "Creditors": [
                {
                    "Reference": "86671a4add96f7a9cc569a6a4a601b38",
                    "Amount": 17.95,
                    "CurrencyCode": "GBP",
                    "IBAN": "GB00CUBK44556687654321",
                    "BBAN": "CUBK44556687654321",
                    "CUID": null,
                    "UPIC": null,
                    "AccountName": "Bob Robson",
                    "AccountHolderLabel": "Bob Robson",
                    "InstitutionName": "MyBank"
                }
            ]
        }
    },
    "Nonce": 1634549462
},
{
    "Type": "TransactionRejected",
    "Version": 2,
    "Payload": {
        "TransactionId": "073dca79-13b8-8bf2-b63b-148957caffe9",
        "Status": "Rejected",
        "Scheme": "Transfer",
        "EndToEndTransactionId": "ccec481ee502",
        "Amount": 97.45,
        "TimestampModified": "2018-08-01T23:01:04.635Z",
        "CurrencyCode": "GBP",
        "DebitCreditCode": "Credit",
        "Reference": "06a87c63c88c84b99294d910e283cae6",
        "IsReturn": false,
        "CancellationReason": "Beneficiary account name does not match beneficiary account number",
        "CancellationCode": "CB_AccountNameInvalid",
        "Account": {
            "IBAN": "GB00CUBK11223312345678",
            "BBAN": "CUBK11223312345678"
        },
        "CounterpartAccount": {
            "IBAN": "GB00CUBK44556687654321",
            "BBAN": "CUBK44556687654321"
        }
    },
    "Nonce": 748392098
},
{
  "Type": "InboundHeldTransaction",
  "Version": 1,
  "Payload": {
    "TimestampCreated": "2019-03-01T00:00:00Z",
    "Scheme": "FasterPayments",
    "Account": {
      "BBAN": "CUBK11223312345678",
      "IBAN": "GB00CUBK11223312345678"
    },
    "CounterpartAccount": {
      "BBAN": "CUBK44556687654321",
      "IBAN": "GB00CUBK44556687654321"
    },
    "TransactionAmount": 88.52,
    "PaymentReference": "ee9a790ea56c142c6b538916c8bd6bcc",
    "EndToEndTransactionId": "5e30e0b4bfb0"
  },
  "Nonce": 1082937278
},
{
"Type": "OutboundHeldTransaction",
"Version": 1,
"Payload":
{
  "TimestampCreated": "2019-03-01T00:00:00Z",
  "Scheme": "FasterPayments",
  "CounterpartAccount": {
    "BBAN": "CUBK44556687654321",
    "IBAN": "GB00CUBK44556687654321"
},
  "Account": {
    "BBAN": "CUBK11223312345678",
    "IBAN": "GB00CUBK11223312345678"
},
  "TransactionAmount": 88.52,
  "PaymentReference": "a6f3c732a6a0b2a8018a06e10c6ecae2",
  "EndToEndTransactionId": "b0c50dc87f86"
},
  "Nonce": 1089558378
}
]""", SampleIsList=true>

module EU =
    type internal ClearBankPaymentJson = FSharp.Data.JsonProvider<"""[
{
   "Type":"CustomerAccounts.TransactionCompleted",
   "Version":1,
   "Payload":{
      "TransactionId":"70e7d2ff-bbb2-4f1f-90f0-f2221029447a",
      "EndToEndIdentification":"Inbound Payment",
      "CreatedDateTime":"2025-02-10T12:49:55.5733333Z",
      "CompletedDateTime":"2025-02-10T12:49:55.6Z",
      "ClearingChannel":"Target2",
      "DebitCreditCode":"Credit",
      "Amount":1000.0,
      "Currency":"EUR",
      "RemittanceInformation":"75030e66dc7844ed9e4a1266c9e8410f",
      "DebtorAccount":{
         "AccountId":null,
         "Iban":"NL17CLRB0126793871",
         "Bban":null,
         "Descriptor":null
      },
      "CreditorAccount":{
         "AccountId":"5602a737-675f-4c85-bbc1-d5d4b4478e64",
         "Iban":"NL25CLRB0066110527",
         "Bban":null,
         "Descriptor":null
      }
   },
   "Nonce":944609635
},{
   "Type":"CustomerAccounts.TransactionCompleted",
   "Version":1,
   "Payload":{
      "TransactionId":"a94a49f7-9007-419a-8722-bc45b0ddc7ec",
      "EndToEndIdentification":"46189668732340195406845",
      "CreatedDateTime":"2025-02-20T10:44:06.7833333Z",
      "CompletedDateTime":"2025-02-20T10:44:09.7633333Z",
      "ClearingChannel":"Target2",
      "DebitCreditCode":"Debit",
      "Amount":424.0,
      "Currency":"EUR",
      "RemittanceInformation":"Example Remittance",
      "DebtorAccount":{
         "AccountId":"c182ac1e-3f15-4b58-b9c3-22719a6b6461",
         "Iban":"NL74CLRB0113702695",
         "Bban":null,
         "Descriptor":null
      },
      "CreditorAccount":{
         "AccountId":null,
         "Iban":"NL91ABNA0417164300",
         "Bban":null,
         "Descriptor":null
      }
   },
   "Nonce":1269251375
},{
   "Type":"CustomerAccounts.TransactionCompleted",
   "Version":1,
   "Payload":{
      "TransactionId":"ce57422c-1276-4820-b97b-e7108f9c59cd",
      "EndToEndIdentification":"Inbound12345677",
      "CreatedDateTime":"2025-02-10T12:54:58.0766667Z",
      "CompletedDateTime":"2025-02-10T12:54:58.0966667Z",
      "ClearingChannel":"SEPA",
      "DebitCreditCode":"Credit",
      "Amount":1000.0,
      "Currency":"EUR",
      "RemittanceInformation":"Funding",
      "DebtorAccount":{
         "AccountId":null,
         "Iban":"NL51XBLT6087402426",
         "Bban":null,
         "Descriptor":null
      },
      "CreditorAccount":{
         "AccountId":"5602a737-675f-4c85-bbc1-d5d4b4478e64",
         "Iban":"NL25CLRB0066110527",
         "Bban":null,
         "Descriptor":null
      }
   },
   "Nonce":740685879
},
{
   "Type":"CustomerAccounts.TransactionCompleted",
   "Version":1,
   "Payload":{
      "TransactionId":"2a259909-e2b5-4bb5-ba91-5e40070ce901",
      "EndToEndIdentification":"4751967232218871296240",
      "CreatedDateTime":"2024-07-29T09:56:59.58Z",
      "CompletedDateTime":"2024-07-29T10:00:23.82Z",
      "ClearingChannel":"SEPA",
      "DebitCreditCode":"Debit",
      "Amount":224.0,
      "Currency":"EUR",
      "RemittanceInformation":"deposit",
      "DebtorAccount":{
         "AccountId":"c182ac1e-3f15-4b58-b9c3-22719a6b6461",
         "Iban":"NL74CLRB0113702695",
         "Bban":null,
         "Descriptor":null
      },
      "CreditorAccount":{
         "AccountId":null,
         "Iban":"NL32INGB0000092273",
         "Bban":null,
         "Descriptor":null
      }
   },
   "Nonce":437327901
},{
   "Type":"CustomerAccounts.TransactionCompleted",
   "Version":1,
   "Payload":{
      "TransactionId":"8fa1ceea-e592-480c-26fa-1fd040c89df1",
      "EndToEndIdentification":"0947eaf6-2b51-496b-afba-969a0a72f38",
      "CreatedDateTime":"2025-02-10T15:54:09.97Z",
      "CompletedDateTime":"2025-02-10T15:54:14.29Z",
      "ClearingChannel":"SEPA-Instant",
      "DebitCreditCode":"Debit",
      "Amount":100.0,
      "Currency":"EUR",
      "RemittanceInformation":"testing instant",
      "DebtorAccount":{
         "AccountId":"5602a737-675f-4c85-bbc1-d5d4b4478e64",
         "Iban":"NL25CLRB0066110527",
         "Bban":null,
         "Descriptor":null
      },
      "CreditorAccount":{
         "AccountId":null,
         "Iban":"DE25370502991000122343",
         "Bban":null,
         "Descriptor":null
      }
   },
   "Nonce":1778087803
},
{
    "Type": "Sepa.Ct.OutboundPayment.ReturnCompleted",
    "Version": 1,
    "Payload": {
        "PaymentId": "061b687d-ef1e-4979-a086-669905545247",
        "TransactionId": "44abdea4-92f9-472a-975d-f5c901cc090e",
        "Reason": "AC06",
        "EndToEndIdentification": "tg8lHRTzQUX7HXKqCoGoJa5pwZOSKSnlgNE"
    },
    "Nonce": 593865028
},{
    "Type": "Sepa.Ct.OutboundPayment.RecallRejected",
    "Payload": {
        "PaymentId": "f9902c78-f25d-4fb7-b29f-68cb3c0caf01",
        "Reason": "CUST",
        "EndToEndIdentification": "e20w26d5cxbg15"
    },
    "Nonce": 185447285
},{
    "Type":"Sepa.Ct.InboundPayment.Recalled",
    "Version": 1,
    "Payload": {
        "PaymentId": "31d42c5d-fa7f-4385-86e3-1ee9e2d60142",
        "CancellationReasonInformation": {
            "Reason": "CODE",
            "AdditionalInformation": "AdditionalInformation",
            "EndToEndIdentification": "5v8flwwosny59jsdg9t"
        }
    },
    "Nonce": 185769097
},{
    "Type": "Sepa.Ct.OutboundPayment.Completed",
    "Version": 1,
    "Payload": {
        "PaymentId": "061b687d-ef1e-4979-a086-669905545247",
        "TransactionId": "44abdea4-92f9-472a-975d-f5c901cc090e",
        "Reason": "AC06",
        "EndToEndIdentification": "f70w45r3cxbg15"
    },
    "Nonce": 163290462
},{
    "Type": "Sepa.Ct.OutboundPayment.Failed",
    "Version": 1,
    "Payload": {
        "PaymentId": "f9902c78-f25d-4fb7-b29f-68cb3c0caf01",
        "Reason": "CODE",
        "AdditionalInformation": "AdditionalInformation",
        "EndToEndIdentification": "f70w45r3cxbg15"
    },
    "Nonce": 185037285
},{
  "Type": "Sepa.Instant.RecallRequest.Received",
  "Version": 2,
  "Payload": {
    "PaymentId": "86a5fcb3-6be3-4bf9-9e8c-dab5b1c39bc4",
    "OriginalTransactionId": "aa4f5da6-15b7-45ec-abf1-c72d58041ec3",
    "ReasonCode": "AC03",
    "TimestampCreated": "2025-01-22T12:54:32.8690907Z",
    "EndToEndId": "QtXXMAu18XRVubaXBSFoLdi14e5uSMzr85p",
    "AdditionalInformation": [
      "Some Reason"
    ]
  },
  "Nonce": 1885237896
},{
  "Type": "Sepa.Instant.RecallPayment.Created",
  "Version": 2,
  "Payload": {
    "RecallTransactionId": "fcb6963b5e9d4b3b943b1551bdc9b398",
    "OriginalTransactionId": "2e4880ab-21a3-43d1-95a1-b6c5426cd6e4",
    "TotalReturnedAmount": 100,
    "TimestampCreated": "2025-01-22T12:54:39.9844016Z",
    "PaymentId": "c7c4402a-56a6-4382-a31a-0ca32099ca4f",
    "EndToEndId": "8ufIvXRF6gDpWcFnLl4GruLZLQsAGCzFa4F"
  },
  "Nonce": 2068152292
},{
  "Type": "Sepa.Instant.RecallPayment.Settled",
  "Version": 2,
  "Payload": {
    "PaymentId": "4f268644-49b1-29f7-238a-d5ee8ea014eb",
    "RecallTransactionId": "c8634f01290904468afeabb417f5ceb0",
    "OriginalTransactionId": "4f26864449b129f7238ad5ee8ea014eb",
    "TotalReturnedAmount": 3.28,
    "TimestampSettled": "2025-01-22T12:55:40.8714037Z",
    "EndToEndId": "mtsHGw83TX9DSvhHD3XDXIHHb3HSqING0T9"
  },
  "Nonce": 445427771
},{
  "Type": "Sepa.Instant.RecallPayment.Cancelled",
  "Version": 2,
  "Payload": {
    "PaymentId": "160b3baf-1d92-1b2a-3826-b022363d865a",
    "RecallTransactionId": "7509f28206a317cfc9ca7ab71f3e8286",
    "OriginalTransactionId": "160b3baf1d921b2a3826b022363d865a",
    "TimestampCancelled": "2025-01-22T12:56:00.3080782Z",
    "CancellationCode": "CB01",
    "CancellationReason": "Unknown error",
    "EndToEndId": "6jff6aBJ1B76Zoy3rvMT5MzImos14R5WbdC"
  },
  "Nonce": 643704746
},{
  "Type": "Sepa.Instant.Inbound.Payment.Settled",
  "Version": 1,
  "Payload": {
    "TransactionId": "8d3d36fe-272c-fc03-44dc-d76c950867e4",
    "PaymentId": "8d3d36fe-272c-fc03-44dc-d76c950867e4",
    "DebtorName": "Thelma Block",
    "DebtorAccount": "GB68INST20182441739477",
    "CreditorName": "Shawna Kautzer",
    "CreditorAccount": "NL37CLRB0126383492",
    "InstructedAmount": 3.35,
    "SchemePaymentMethod": "INST",
    "TimestampCreated": "2025-01-22T12:55:12.1941864Z",
    "TimestampSubmitted": "2025-01-22T12:55:12.1941864Z",
    "EndToEndId": "ec1GSkA8YCLzPYod96O7ROildNHeeHnuwgO",
    "AdditionalPaymentProperties": [
      {
        "Key": "CreditorAgentBic",
        "Value": "RZKTAT2K288"
      }
    ]
  },
  "Nonce": 1225137631
}


]""", SampleIsList=true>


type ClearBankPaymentUK = UK.ClearBankPaymentJson.Root
type ClearBankPaymentEU = EU.ClearBankPaymentJson.Root

let parsePaymentsCallUK (webhookInput:string) : ClearBankPaymentUK =
        UK.ClearBankPaymentJson.Load (Serializer.Deserialize webhookInput)

let parsePaymentsCallEU (webhookInput:string) : ClearBankPaymentEU =
        EU.ClearBankPaymentJson.Load (Serializer.Deserialize webhookInput)

module WebhookTypes =
    module UK =
        /// UK Payment sent successfully.
        [<Literal>]
        let TransactionSettled = "TransactionSettled"
        /// UK Payment has incorrect data, validation failed.
        [<Literal>]
        let PaymentMessageAssessmentFailed = "PaymentMessageAssessmentFailed"
        /// UK Payment has incorrect data, validation failed. Same as PaymentMessageAssessmentFailed but due to typing error, the webhook can return either. 
        [<Literal>]
        let PaymentMessageAssesmentFailed = "PaymentMessageAssesmentFailed"
        /// UK Payment validation failed.
        [<Literal>]
        let PaymentMessageValidationFailed = "PaymentMessageValidationFailed"
        /// UK Payment sent but returned as failed.
        [<Literal>]
        let TransactionRejected = "TransactionRejected"
        /// UK
        [<Literal>]
        let InboundHeldTransaction = "InboundHeldTransaction"
        /// UK
        [<Literal>]
        let OutboundHeldTransaction = "OutboundHeldTransaction"

    module EU =
        /// EU
        [<Literal>]
        let ``CustomerAccounts.TransactionCompleted`` = "CustomerAccounts.TransactionCompleted"

        /// EU SEPA
        [<Literal>]
        let ``Sepa.Ct.InboundPayment.ReturnCompleted`` = "Sepa.Ct.InboundPayment.ReturnCompleted"
        /// EU SEPA
        [<Literal>]
        let ``Sepa.Ct.InboundPayment.ReturnFailed`` = "Sepa.Ct.InboundPayment.ReturnFailed"
        /// EU SEPA
        [<Literal>]
        let ``Sepa.Ct.OutboundPayment.ReturnCompleted`` = "Sepa.Ct.OutboundPayment.ReturnCompleted"
        /// EU SEPA
        [<Literal>]
        let ``Sepa.Ct.OutboundPayment.ReturnFailed`` = "Sepa.Ct.OutboundPayment.ReturnFailed"

        /// EU SEPA Instant
        [<Literal>]
        let ``Sepa.Instant.Inbound.Payment.Settled`` = "Sepa.Instant.Inbound.Payment.Settled"
        /// EU SEPA Instant
        [<Literal>]
        let ``Sepa.Instant.RecallRequest.Received`` = "Sepa.Instant.RecallRequest.Received"
        /// EU SEPA Instant
        [<Literal>]
        let ``Sepa.Instant.RecallPayment.Created`` = "Sepa.Instant.RecallPayment.Created"
        /// EU SEPA Instant
        [<Literal>]
        let ``Sepa.Instant.RecallPayment.Settled`` = "Sepa.Instant.RecallPayment.Settled"
        /// EU SEPA Instant
        [<Literal>]
        let ``Sepa.Instant.RecallPayment.Cancelled`` = "Sepa.Instant.RecallPayment.Cancelled"

        /// EU T2
        [<Literal>]
        let ``Target2.FICreditTransfer.Inbound.Completed`` = "Target2.FICreditTransfer.Inbound.Completed"
        /// EU T2
        [<Literal>]
        let ``Target2.FICreditTransfer.Outbound.Completed`` = "Target2.FICreditTransfer.Outbound.Completed"
        /// EU T2
        [<Literal>]
        let ``Target2.FICreditTransfer.Outbound.Failed`` = "Target2.FICreditTransfer.Outbound.Failed"

/// UK webhook types, from https://clearbank.github.io/uk/docs/lookup/webhook-lookup-table
[<RequireQualifiedAccess>]
type UkWebhook =
    | TransactionSettled
    | TransactionRejected
    /// Also sent as PaymentMessageAssesmentFailed.
    | PaymentMessageAssessmentFailed
    | PaymentMessageValidationFailed
    | InboundHeldTransaction
    /// Also listed as OuboundHeldTransaction.
    | OutboundHeldTransaction
    | AccountCreated
    | AccountClosureCompleted
    | AccountClosureFailed
    | VirtualAccountCreated
    | VirtualAccountCreationFailed
    | BacsMandateInitiated
    | BacsMandateInitiationFailed
    | BacsMandateCancelled
    | BacsMandateCancellationFailed
    | BacsMandateMigrated
    | BacsMandateReturned
    | BacsMandateReturnFailed
    | BacsDirectDebitInboundPaymentCreated
    | BacsDirectCreditInboundPaymentCreated
    | BacsDirectCreditInboundPaymentHeld
    | BacsDirectDebitReturnCreated
    | BacsDirectCreditReturnCreated
    | BacsDirectCreditRecalled
    | BacsDirectDebitIndemnityClaimReceived
    | BacsUnpaidDirectDebitReceived
    | BacsUnappliedCreditReceived
    | BacsDebitContraReceived
    | StatementsReadyForDownload
    | InterestPaid
    | FITestEvent
    /// A type this library doesn't list, e.g. multi-currency, cheque, ISA, FX or customer onboarding webhooks.
    | Unlisted of string

module UkWebhook =

    let private names =
        [ UkWebhook.TransactionSettled, "TransactionSettled"
          UkWebhook.TransactionRejected, "TransactionRejected"
          UkWebhook.PaymentMessageAssessmentFailed, "PaymentMessageAssessmentFailed"
          UkWebhook.PaymentMessageValidationFailed, "PaymentMessageValidationFailed"
          UkWebhook.InboundHeldTransaction, "InboundHeldTransaction"
          UkWebhook.OutboundHeldTransaction, "OutboundHeldTransaction"
          UkWebhook.AccountCreated, "AccountCreated"
          UkWebhook.AccountClosureCompleted, "AccountClosureCompleted"
          UkWebhook.AccountClosureFailed, "AccountClosureFailed"
          UkWebhook.VirtualAccountCreated, "VirtualAccountCreated"
          UkWebhook.VirtualAccountCreationFailed, "VirtualAccountCreationFailed"
          UkWebhook.BacsMandateInitiated, "BacsMandateInitiatedV2"
          UkWebhook.BacsMandateInitiationFailed, "BacsMandateInitiationFailed"
          UkWebhook.BacsMandateCancelled, "BacsMandateCancelledV2"
          UkWebhook.BacsMandateCancellationFailed, "BacsMandateCancellationFailed"
          UkWebhook.BacsMandateMigrated, "BacsMandateMigratedV2"
          UkWebhook.BacsMandateReturned, "BacsMandateReturned"
          UkWebhook.BacsMandateReturnFailed, "BacsMandateReturnFailed"
          UkWebhook.BacsDirectDebitInboundPaymentCreated, "BacsDirectDebitInboundPaymentCreatedV2"
          UkWebhook.BacsDirectCreditInboundPaymentCreated, "BacsDirectCreditInboundPaymentCreatedV2"
          UkWebhook.BacsDirectCreditInboundPaymentHeld, "BacsDirectCreditInboundPaymentHeldV3"
          UkWebhook.BacsDirectDebitReturnCreated, "BacsDirectDebitReturnCreatedV4"
          UkWebhook.BacsDirectCreditReturnCreated, "BacsDirectCreditReturnCreatedV4"
          UkWebhook.BacsDirectCreditRecalled, "BacsDirectCreditRecalledV1"
          UkWebhook.BacsDirectDebitIndemnityClaimReceived, "BacsDirectDebitIndemnityClaimReceivedEventV3"
          UkWebhook.BacsUnpaidDirectDebitReceived, "BacsUnpaidDirectDebitReceivedV3"
          UkWebhook.BacsUnappliedCreditReceived, "BacsUnappliedCreditReceivedV3"
          UkWebhook.BacsDebitContraReceived, "BacsDebitContraReceived"
          UkWebhook.StatementsReadyForDownload, "Statements.ReadyForDownload"
          UkWebhook.InterestPaid, "InterestPaid"
          UkWebhook.FITestEvent, "FITestEvent" ]

    let private byName =
        (names |> List.map (fun (case, name) -> name, case))
        @ [ WebhookTypes.UK.PaymentMessageAssesmentFailed, UkWebhook.PaymentMessageAssessmentFailed
            "OuboundHeldTransaction", UkWebhook.OutboundHeldTransaction ]
        |> readOnlyDict

    /// The webhook type of a payload's Type field.
    let parse (name: string) =
        match byName.TryGetValue name with
        | true, case -> case
        | false, _ -> UkWebhook.Unlisted name

    /// The Type field value.
    let name =
        function
        | UkWebhook.Unlisted name -> name
        | case -> names |> List.find (fst >> (=) case) |> snd

/// EU webhook types.
[<RequireQualifiedAccess>]
type EuWebhook =
    | CustomerAccountsTransactionCompleted
    | SepaCtInboundPaymentReturnCompleted
    | SepaCtInboundPaymentReturnFailed
    | SepaCtOutboundPaymentReturnCompleted
    | SepaCtOutboundPaymentReturnFailed
    | SepaInstantInboundPaymentSettled
    | SepaInstantRecallRequestReceived
    | SepaInstantRecallPaymentCreated
    | SepaInstantRecallPaymentSettled
    | SepaInstantRecallPaymentCancelled
    | Target2InboundCompleted
    | Target2OutboundCompleted
    | Target2OutboundFailed
    | FITestEvent
    | Unlisted of string

module EuWebhook =

    let private names =
        [ EuWebhook.CustomerAccountsTransactionCompleted, WebhookTypes.EU.``CustomerAccounts.TransactionCompleted``
          EuWebhook.SepaCtInboundPaymentReturnCompleted, WebhookTypes.EU.``Sepa.Ct.InboundPayment.ReturnCompleted``
          EuWebhook.SepaCtInboundPaymentReturnFailed, WebhookTypes.EU.``Sepa.Ct.InboundPayment.ReturnFailed``
          EuWebhook.SepaCtOutboundPaymentReturnCompleted, WebhookTypes.EU.``Sepa.Ct.OutboundPayment.ReturnCompleted``
          EuWebhook.SepaCtOutboundPaymentReturnFailed, WebhookTypes.EU.``Sepa.Ct.OutboundPayment.ReturnFailed``
          EuWebhook.SepaInstantInboundPaymentSettled, WebhookTypes.EU.``Sepa.Instant.Inbound.Payment.Settled``
          EuWebhook.SepaInstantRecallRequestReceived, WebhookTypes.EU.``Sepa.Instant.RecallRequest.Received``
          EuWebhook.SepaInstantRecallPaymentCreated, WebhookTypes.EU.``Sepa.Instant.RecallPayment.Created``
          EuWebhook.SepaInstantRecallPaymentSettled, WebhookTypes.EU.``Sepa.Instant.RecallPayment.Settled``
          EuWebhook.SepaInstantRecallPaymentCancelled, WebhookTypes.EU.``Sepa.Instant.RecallPayment.Cancelled``
          EuWebhook.Target2InboundCompleted, WebhookTypes.EU.``Target2.FICreditTransfer.Inbound.Completed``
          EuWebhook.Target2OutboundCompleted, WebhookTypes.EU.``Target2.FICreditTransfer.Outbound.Completed``
          EuWebhook.Target2OutboundFailed, WebhookTypes.EU.``Target2.FICreditTransfer.Outbound.Failed``
          EuWebhook.FITestEvent, "FITestEvent" ]

    let private byName = names |> List.map (fun (case, name) -> name, case) |> readOnlyDict

    /// The webhook type of a payload's Type field.
    let parse (name: string) =
        match byName.TryGetValue name with
        | true, case -> case
        | false, _ -> EuWebhook.Unlisted name

    /// The Type field value.
    let name =
        function
        | EuWebhook.Unlisted name -> name
        | case -> names |> List.find (fst >> (=) case) |> snd

/// The typed webhook type of a parsed UK payload.
let webhookTypeUK (payload: ClearBankPaymentUK) = UkWebhook.parse payload.Type

/// The typed webhook type of a parsed EU payload.
let webhookTypeEU (payload: ClearBankPaymentEU) = EuWebhook.parse payload.Type

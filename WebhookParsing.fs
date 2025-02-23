module ClearBankWebhooks
open System
open FSharp.Data.JsonProvider

[<Sealed>]
type WebHookResponse (nonce:int64) =
    member val Nonce = nonce |> Convert.ToInt32

let internal cbSerializerSettings = Newtonsoft.Json.JsonSerializerSettings(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)

/// Creates a plain text response
let createResponsePlain (nonce:int64) =
    Newtonsoft.Json.JsonConvert.SerializeObject(WebHookResponse nonce, cbSerializerSettings)

/// Creates a valid response that will not escape JSON object strings
let createResponse config azureKeyVaultCertificateName (request:System.Net.Http.HttpRequestMessage) nonce =
    task {
        let responseContent = nonce |> createResponsePlain

        let! signature = ClearBank.calculateSignature config azureKeyVaultCertificateName responseContent

        // The purpose of StringContent over CreateResponse() is to avoid string escaping.
        let response =
            new System.Net.Http.HttpResponseMessage(
                System.Net.HttpStatusCode.OK,
                RequestMessage = request,
                Content = new System.Net.Http.StringContent(responseContent, System.Text.Encoding.UTF8, "application/json"))

        response.Headers.Add("DigitalSignature", signature)
        return response
    }

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

type ClearBankPayment = ClearBankPaymentJson.Root

let parsePaymentsCall (webhookInput:string) : ClearBankPayment =
        ClearBankPaymentJson.Load (Serializer.Deserialize webhookInput)

module WebhookTypes =
    /// Payment sent successfully.
    let TransactionSettled = "TransactionSettled"
    /// Payment has incorrect data, validation failed.
    let PaymentMessageAssessmentFailed = "PaymentMessageAssessmentFailed"
    /// Payment has incorrect data, validation failed. Same as PaymentMessageAssessmentFailed but due to typing error, the webhook can return either. 
    let PaymentMessageAssesmentFailed = "PaymentMessageAssesmentFailed"
    /// Payment validation failed.
    let PaymentMessageValidationFailed = "PaymentMessageValidationFailed"
    /// Payment sent but returned as failed.
    let TransactionRejected = "TransactionRejected"
    let InboundHeldTransaction = "InboundHeldTransaction"
    let OutboundHeldTransaction = "OutboundHeldTransaction"

module ClearBankWebhooks

let createResponse (nonce:int64) = """{"Nonce": """ + nonce.ToString() + "}"

type ClearBankPaymentJson = FSharp.Data.JsonProvider<"""[
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
        "Reference": "51dcf7ba480e61c5a60bbb6c6b774d17",
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
}
]""", SampleIsList=true>
type ClearBankPayment = ClearBankPaymentJson.Root

let parsePaymentsCall (webhookInput:string) : ClearBankPayment =
        ClearBankPaymentJson.Parse webhookInput

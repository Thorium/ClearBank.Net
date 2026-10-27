
# ClearBank.NET

Unofficial .NET client for ClearBank integration, creating online payments via their API.
Bank payment handling automation in the United Kingdom (UK/GB) and European Union (EU). Released as a [NuGet package](https://www.nuget.org/packages/ClearBank.Net/).
This aims to be bare and easy.

The library has been structured as UK/UK.Multicurrency/EU, and aims to cover major use cases from [ClearBank developer portal](https://clearbank.github.io/uk) with simple setup in both Microsoft DotNet and Microsoft .NET Framework.


Here are the pre-conditions:

## 1. Create Azure KeyVault

From the Access Policies, add the role, and "Key Permissions" for the Sign & Verify.
Azure KeyVault supports HSM (hardware security module) backed keys.

## 2. Configure the Certificate -tab

For example, a new self-signed certificate can be used, but the PEM format has to be selected.

Download the certificate, a PEM-file.

## 3. Create a CSR with Open-SSL and upload it to the ClearBank portal

```shell
openssl.exe req -new -sha256 -key "c:\temp\downloaded.pem" -out file.csr
```

-	Upload that to the portal https://institution-sim.clearbank.co.uk/
-	Copy the private key (the long string) from the message box. You need that in the config later, as that will be used in the POST header `Authorization: Bearer (the long string)`

_Update: You can also get CSR now directly from Azure keyvault, by selecting the certificate and clicking "Certificate Operation" -> "Download CSR"_

## (4. Optional: Ensure the correctness with FI-API-Signtool)

The signing is done similarly to this repo, but the official code is more messy because it's C#.
[GitHub - clearbank/fi-api-signtool](https://github.com/clearbank/fi-api-signtool)

## 5. Start using this library

Your user code should look something like this:

```fsharp

let doSomeTransactions =
    let clearbankDefaultConfig =
        {
            BaseUrl = "https://institution-api-sim.clearbank.co.uk/"
            PrivateKey = "..."
            AzureKeyVaultName =  "myVault"
            AzureKeyVaultCredentials = DefaultCredentials
        } : ClearBank.Common.ClearbankConfiguration

    let azureKeyVaultCertificateName = "my-cert"
    let fromAccount = ClearBank.Common.UK_Domestic("60-01-34", "51112345")

    let target1 = 
        ClearBank.UK.createCreditTransfer
            {
                To = ClearBank.Common.UK_Domestic("20-20-15", "55555555")
                AccountHolder = "Mr Test"
                Sum = 123.00m
                Currency = "GBP"
                Description = "Phone Bill"
                PaymentReference = "123456789"
                TransactionId = "12345" // End-to-end: You identify corresponding webhooks with this.
            }

    let target2 = 
        ClearBank.UK.createCreditTransfer
            {
                To = ClearBank.Common.UK_Domestic("40-47-84", "70872490")
                AccountHolder = "John Doe"
                Sum = 123.00m
                Currency = "GBP"
                Description = "Some money"
                PaymentReference = "12345"
                TransactionId = "12345"
            }

    let xReqId = Guid.NewGuid()
    let instructions = ClearBank.UK.createPaymentInstruction "Batch123" fromAccount  [| target1 ; target2 |]
    ClearBank.UK.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xReqId [| instructions |] |> Async.RunSynchronously

```

If you have problems with the KeyVault authentication, you can change the AzureKeyVaultCredentials in the config

```fsharp
    let clearbankDefaultConfig =
        {
            BaseUrl = "https://institution-api-sim.clearbank.co.uk/"
            PrivateKey = "..."
            AzureKeyVaultName =  "..."
            AzureKeyVaultCertificateName = "..."
            AzureKeyVaultCredentials =
                CredentialsWithOptions (
                    Azure.Identity.DefaultAzureCredentialOptions (
                        //ExcludeEnvironmentCredential = true
                        //,ExcludeManagedIdentityCredential = true
                        ExcludeSharedTokenCacheCredential = true
                        ,ExcludeVisualStudioCredential = true
                        //,ExcludeVisualStudioCodeCredential = true
                        //,ExcludeAzureCliCredential = true
                        //,ExcludeInteractiveBrowserCredential = true
                    )) 
            LogUnsuccessfulHandler = None
        } : ClearBank.Common.ClearbankConfiguration
```

The last `LogUnsuccessfulHandler` property is an optional error-logging callback. You could replace it e.g. with `Some logging` and have a function:

```fsharp
    let logging(status,content) =
        match ClearBank.Common.parseClearBankErrorContent content with
        | ClearBankEmptyResponse -> Console.WriteLine "Response was empty"
        | ClearBankTransactionError errors -> errors |> Seq.iter(fun (tid,err) -> Console.WriteLine("Transaction id " + tid + " failed for " + err))
        | ClearBankGeneralError(title, detail) -> Console.WriteLine(title + ", " + detail)
        | ClearBankUnknownError content -> Console.WriteLine("JSON: " + content)
```

### Creating accounts

There is a method `ClearBank.UK.createNewAccount` to create new accounts.

### Getting accounts and transactions

There are methods `ClearBank.UK.getAccounts`, that you can use for e.g. getting balances, 
and `ClearBank.UK.getTransactions` config pageSize pageNumber startDate endDate

### Shared client, pluggable signing and the Task API

The functions above take the configuration and create an `HttpClient` per call, signing with Azure Key Vault.
For a server, create one `ClearBankClient` for the application's lifetime and use the `Api` modules
(`ClearBank.UK.Api`, `ClearBank.UK.MultiCurrency.Api`, `ClearBank.EU.Api`). They return `Task<Result<'T, exn * string>>`,
take a `CancellationToken`, and share the caller's `HttpClient`.

```fsharp
open System
open ClearBank.Common
open ClearBank.UK

// DigitalSignature: RSASSA-PKCS1-v1_5 with SHA-256 over the UTF-8 body. Pick a signer:
let signer = RequestSigner.azureKeyVault DefaultCredentials "myVault" "my-cert"
// let signer = RequestSigner.rsa rsaPrivateKey            // a local System.Security.Cryptography.RSA
// let signer = RequestSigner.ofDigest signDigestWithKms   // a remote key that signs a SHA-256 digest

// Either your own HttpClient with BaseAddress set (e.g. from IHttpClientFactory)...
let client = ClearBankClient.create httpClient "the API token" signer
// ...or one built from the configuration, with its LogUnsuccessfulHandler:
// let client = ClearBankClient.fromConfig clearbankDefaultConfig signer

task {
    let! accounts = Api.getAccountsAsync client ValueNone ValueNone ct

    let! payee =
        Api.verifyPayeeAsync client (Guid.NewGuid())
            { SortCode = "20-20-15"; AccountNumber = "55555555"; OwnerName = ",,John Doe"
              LegalOwnerType = LegalOwnerType.Personal; SecondaryReference = ValueNone; EndToEndId = ValueNone } ct

    match payee with
    | Ok check when check.Matched -> () // pay with Api.transferPaymentsAsync
    | Ok check -> () // show check.Name / check.ReasonCode to the payer before paying
    | Error (err, details) -> ()
}
```

AWS KMS needs no AWS dependency in this library; sign the digest in your own code:

```fsharp
let kmsSigner (kms: Amazon.KeyManagementService.IAmazonKeyManagementService) keyId =
    RequestSigner.ofDigest (fun digest ct ->
        task {
            let! response =
                kms.SignAsync(
                    Amazon.KeyManagementService.Model.SignRequest(
                        KeyId = keyId,
                        Message = new IO.MemoryStream(digest),
                        MessageType = Amazon.KeyManagementService.MessageType.DIGEST,
                        SigningAlgorithm = Amazon.KeyManagementService.SigningAlgorithmSpec.RSASSA_PKCS1_V1_5_SHA_256), ct)
            return response.Signature.ToArray()
        })
```

From C#: `RequestSigner.FromRsa(rsa)`, `RequestSigner.FromDigest((digest, ct) => ...)`, `RequestSigner.Create((body, ct) => ...)`, `ClearBankClient.Create(httpClient, token, signer)`,
then e.g. `await UK.Api.getAccountsAsync(client, FSharpValueOption<int>.None, FSharpValueOption<int>.None, ct)`.

`ClearBank.UK.Api` operations (real accounts and, through `AccountRef.Virtual`, virtual accounts):

| Area | Functions |
|---|---|
| Accounts | `createAccountAsync`, `getAccountsAsync`, `getAccountAsync`, `updateAccountAsync` (status, reason, owner, minimum balance), `closeAccountAsync` |
| Interest-bearing account closure | `requestAccountClosureAsync` (asynchronous; interest goes to the nominated account), `getAccountClosureAsync` |
| Transactions | `getTransactionsAsync`, `getAccountTransactionsAsync`, `getAccountTransactionAsync`, `returnTransactionsAsync` (Bacs returns) |
| Bacs | `getUnpaidDirectDebitsAsync`, `getUnappliedDirectCreditsAsync` |
| Virtual accounts | `createVirtualAccountsAsync`, `getVirtualAccountsAsync`, `getAllVirtualAccountsAsync`, `getVirtualAccountAsync`, `updateVirtualAccountAsync`, `disableVirtualAccountAsync` |
| Direct Debit mandates | `getMandatesAsync`, `cancelMandateAsync`, `returnMandateAsync`, `getMandateCollectionsAsync`, `getUnpaidCollectionsAsync` |
| Payments | `transferPaymentsAsync` (Faster Payments; payments to accounts held at ClearBank settle as internal transfers) |
| CHAPS | `chapsCustomerPaymentAsync`, `chapsInstitutionPaymentAsync`, `chapsReturnPaymentAsync` (v6; build requests with `ChapsV6` types and accounts with `UK.chapsAccount`) |
| Confirmation of Payee | `verifyPayeeAsync`, `requiresSecondaryReferenceAsync`, `setCopParticipationAsync` (inbound opt-out) |
| Test | `testAsync` |

`ClearBank.UK.MultiCurrency.Api` also has `cancelPaymentsAsync`, and `ClearBank.EU.Api` covers the SEPA and Target2 calls.
Each `requestId` becomes the `X-Request-Id` header: use a new one per request and keep it with your record of the request.

Not covered yet: FPS returns and single payments, statements, embedded banking (customers, KYC, ISAs, savings) and multi-currency virtual accounts.

The schemas in `Schemas/` are copies of ClearBank's [published endpoints](https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints).
Local edits: `clearbank-api-v1.json` and `clearbank-api-v2.json` have response keys trimmed and a malformed `examples` and query `uniqueItems` removed
for the strict OpenAPI reader; `chaps-v6.json` and `mccy-initiate-payment-v1-dec21.json` declare the payment amount as `double`,
because SwaggerProvider maps other number formats to `float32`.

### Webhook responses

To receive webhooks, you need a web server, which is outside the scope of this library.
However, there are some helper classes provided in this library.

To use those, your server code could look something like this:

```fsharp
type CBWebhookController() as this =
    inherit ApiController()

    member __.Post ()
        async {
            // 1. Verify the webhook against your ClearBank public key:

            // Download the public key (a .pem file) from your ClearBank portal and use a converter such as https://raskeyconverter.azurewebsites.net/PemToXml to convert the text to XML
            let publicKeyXml = "<RSAKeyValue>...</RSAKeyValue>"

            let signature = this.Request.Headers.GetValues("DigitalSignature") |> Seq.tryHead |> Option.map Convert.FromBase64String //add some error handling
            let! bodyJson = this.Request.Content.ReadAsStringAsync() |> Async.AwaitTask

            let! isVerified = ClearBank.Common.verifySignature publicKeyXml signature bodyJson //proceed only if true

            // 2. Parse and handle the request:
            let parsed = ClearBank.Webhooks.parsePaymentsCallUK bodyJson

            // Use parsed.Type to get the webhook type.
            // Different types may have the corresponding end-to-end transactionId in different places.
            // Fetch your transaction based on that ID, and do whatever you want.

            //    match ClearBank.Webhooks.webhookTypeUK parsed with
            //    | ClearBank.Webhooks.UkWebhook.TransactionSettled -> ...
            //    | ClearBank.Webhooks.UkWebhook.PaymentMessageAssessmentFailed -> ...
            //    | ClearBank.Webhooks.UkWebhook.PaymentMessageValidationFailed -> ...
            //    | ClearBank.Webhooks.UkWebhook.TransactionRejected -> ...
            //    | ClearBank.Webhooks.UkWebhook.BacsUnpaidDirectDebitReceived -> ...
            //    | ClearBank.Webhooks.UkWebhook.Unlisted other -> ...
            //    | _ -> (* FITestEvent *) ...
            // The string constants in ClearBank.Webhooks.WebhookTypes still work with parsed.Type.

            // 3. Create response (or Webhooks.createSignedResponseAsync signer this.Request parsed.Nonce ct with any RequestSigner)
            return! ClearBank.Webhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName this.Request parsed.Nonce

        } |> Async.StartAsTask
```

To test webhooks, you can use e.g. Fiddler to compose them and https://webhook.site/ to get their calls.

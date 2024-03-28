
# ClearBank.NET

Unofficial .NET client for ClearBank integration, creating online payments via their API.
Bank payment handling automation in United Kingdom. Released as a [NuGet package](https://www.nuget.org/packages/ClearBank.Net/).
This aims to be bare and easy.

Here are the pre-conditions:

## 1. Create Azure KeyVault

From the Access Policies, add the role, and "Key Permissions" for the Sign & Verify.
Azure KeyVault supports HSM (hardware security module) backed keys.

## 2. Configure the Certificate -tab

Use e.g. new self-signed sertificate, but the PEM format has to be selected.

Download the certificate, a PEM-file.

## 3. Create a CSR with Open-SSL and upload it to the ClearBank portal

```shell
openssl.exe req -new -sha256 -key "c:\temp\downloaded.pem" -out file.csr
```

-	Upload that to the portal https://institution-sim.clearbank.co.uk/
-	Copy the private key (the long string) from the message box. You need that in the config later, as that will be used in POST header `Authorization: Bearer (the long string)`

_Update: You can also get CSR now directly from Azure keyvault, by selecting the certificate and clicking "Certificate Operation" -> "Download CSR"_

## (4. Optional: Ensure the correctness with FI-API-Signtool)

The signing is done similarly than in this repo, but the official code is more messy as it's C#.
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
        } : ClearbankConfigruation

    let azureKeyVaultCertificateName = "my-cert"
    let fromAccount = UK_Domestic("60-01-34", "51112345")

    let target1 = 
        {
            To = UK_Domestic("20-20-15", "55555555")
            AccountHolder = "Mr Test"
            Sum = 123.00m
            Currency = "GBP"
            Description = "Phone Bill"
            PaymentReference = "123456789"
            TransactionId = "12345" // End-to-end: You identify corresponding webhooks with this.
        } |> ClearBank.createCreditTransfer

    let target2 = 
        {
            To = UK_Domestic("40-47-84", "70872490")
            AccountHolder = "John Doe"
            Sum = 123.00m
            Currency = "GBP"
            Description = "Some money"
            PaymentReference = "12345"
            TransactionId = "12345"
        } |> ClearBank.createCreditTransfer

    let xReqId = Guid.NewGuid()
    let instructions = ClearBank.createPaymentInstruction "Batch123" fromAccount  [| target1 ; target2 |]
    ClearBank.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xReqId [| instructions |] |> Async.RunSynchronously

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
        } : ClearbankConfiguration
```

The last `LogUnsuccessfulHandler` property is optional error-logging callback. You could replace it e.g. with `Some logging` and have a function:

```fsharp
    let logging(status,content) =
        match ClearBank.parseClarBankErrorContent content with
        | ClearBankEmptyResponse -> Console.WriteLine "Response was empty"
        | ClearBankTransactionError errors -> errors |> Seq.iter(fun (tid,err) -> Console.WriteLine("Transaction id " + tid + " failed for " + err))
        | ClearBankGeneralError(title, detail) -> Console.WriteLine(title + ", " + detail)
        | ClearBankUnknownError content -> Console.WriteLine("JSON: " + content)
```

### Creating accounts

There is a method `ClearBank.createNewAccount` to create new accounts.

### Getting accounts and transactions

There are methods `ClearBank.getAccounts`, that you can use for e.g. getting balances, 
and `ClearBank.getTransactions` config pageSize pageNumber startDate endDate

### Webhook responses

For receiving webhooks you have to get a web-server which is out of scope of this library,
however there are some helper classes provided in this library.

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

            let! isVerified = ClearBank.verifySignature publicKeyXml signature bodyJson //proceed only if true

            // 2. Parse and handle the request:
            let parsed = ClearBankWebhooks.parsePaymentsCall bodyJson

            // Use parsed.Type to get the webhook type.
            // Different types may have the corresponding end-to-end transactionId in different places.
            // Fetch your transaction based on that id, and do whatever you want.

            //    match parsed.Type with
            //    | "TransactionSettled" -> ...
            //    | "PaymentMessageAssessmentFailed" -> ...
            //    | "PaymentMessageValidationFailed" -> ...
            //    | "TransactionRejected" -> ...
            //    | _ -> (* "FITestEvent" *) ...

            // 3. Create response
            return! ClearBankWebhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName this.Request parsed.Nonce

        } |> Async.StartAsTask
```

To test webhooks you can use e.g. Fiddler to compose them, and https://webhook.site/ to get the their calls.

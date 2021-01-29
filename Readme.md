
# ClearBank.NET

Unofficial .NET client for ClearBank integration, creating online payments via their API.
This aims to be bare and easy.

Here are the pre-conditions:

## 1. Create Azure KeyVault

From the Access Policies, add the role, and "Key Permissions" for the Sign & Verify 

## 2. Configure the Certificate -tab

Use e.g. new self-signed sertificate, but the PEM format has to be selected.

Download the certificate, a PEM-file.

## 3. Create a CSR with Open-SSL and upload it to the ClearBank portal

```shell
openssl.exe req -new -sha256 -key "c:\temp\downloaded.pem" -out file.csr
```

-	Upload that to the portal https://institution-sim.clearbank.co.uk/
-	Copy the private key (the long string) from the message box. You need that in the config later, as that will be used in POST header `Authorization: Bearer (the long string)`


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
            AzureKeyVaultCertificateName = "my-cert"
            AzureKeyVaultCredentials = DefaultCredentials
        } : ClearbankConfigruation

    let fromAccount = UK_Domestic("60-01-34", "51112345")

    let target1 = 
        {
            To = UK_Domestic("60-01-35", "51112346")
            AccountHolder = "Mr Test"
            Sum = 123.00m
            Currency = "GBP"
            Description = "Phone Bill"
            PaymentReference = "123456789"
        } |> ClearBank.createCreditTransfer

    let target2 = 
        {
            To = UK_Domestic("60-01-36", "51112347")
            AccountHolder = "John Doe"
            Sum = 123.00m
            Currency = "GBP"
            Description = "Some money"
            PaymentReference = "12345"
        } |> ClearBank.createCreditTransfer

    let instructions = ClearBank.createPaymentInstruction "Batch123" fromAccount  [| target1 ; target2 |]
    ClearBank.callClearbank clearbankDefaultConfig (Guid.NewGuid()) [| instructions |] |> Async.RunSynchronously

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
        } : ClearbankConfigruation
```




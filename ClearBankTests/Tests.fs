namespace ClearBankTests

open ClearBank
open System
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type TestClass () =

    let logging(status,content) =
        match parseClarBankErrorContent content with
        | ClearBankEmptyResponse -> Console.WriteLine "Response was empty"
        | ClearBankTransactionError errors -> errors |> Seq.iter(fun (tid,err) -> Console.WriteLine("Transaction id " + tid + " failed for " + err))
        | ClearBankGeneralError(title, detail) -> Console.WriteLine(title + ", " + detail)
        | ClearBankUnknownError content -> Console.WriteLine("JSON: " + content)

    let clearbankDefaultConfig =
        {
            BaseUrl = "https://institution-api-sim.clearbank.co.uk/"
            PrivateKey = "..."
            AzureKeyVaultName =  "myVault"
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
            LogUnsuccessfulHandler = Some logging
        } : ClearbankConfiguration

    let azureKeyVaultCertificateName = "myCert"
    let AssertTestResult actual =
        match actual with
        | Ok _ -> Assert.IsTrue true
        | Error (err:Exception,details) ->
            Assert.Fail(err.Message + ", " + details)

    [<TestMethod>]
    member this.TestMethodPassingTest () =
        let actual = callTestEndpoint clearbankDefaultConfig azureKeyVaultCertificateName |> Async.RunSynchronously
        AssertTestResult actual

    [<TestMethod>]
    member this.ProcessPaymentsTest () =

        let expected = Ok ()
        let fromAccount = UK_Domestic("04-06-98", "00000001")

        let target1 = 
            {
                To = UK_Domestic("20-20-15", "55555555")
                AccountHolder = "Mr Test"
                Sum = 123.00m
                Currency = "GBP"
                Description = "Phone Bill"
                PaymentReference = "123456789"
                TransactionId = "123456789"
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

        let xreq = Guid.NewGuid()
        let instructions = ClearBank.createPaymentInstruction "Batch123" fromAccount [| target1; target2 |]
        let actual = ClearBank.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xreq [| instructions |] |> Async.RunSynchronously
        AssertTestResult actual


    [<TestMethod>]
    member this.CreateAccountTest () =
        let actual = ClearBank.createNewAccount clearbankDefaultConfig azureKeyVaultCertificateName (Guid.NewGuid()) "04-06-98" "Test account" None |> Async.RunSynchronously
        AssertTestResult actual

    [<TestMethod>]
    member this.GetAccountsTest () =
        let actual = ClearBank.getAccounts clearbankDefaultConfig |> Async.RunSynchronously
        match actual with
        | Ok x ->
            let accountBalances =
                x.Accounts
                |> Array.collect (fun a ->
                    a.Balances |> Array.map(fun b ->
                        (if a.Name = b.Name then a.Name else a.Name + " - " + b.Name) + ": " +
                        b.Amount.ToString("F") + " " + b.Currency))

            Assert.AreNotEqual(0, accountBalances.Length)
            Assert.AreNotEqual("",accountBalances.[0])
            Assert.AreNotEqual("",String.Join("\r\n", accountBalances))
 
        | Error (err:Exception,details) ->
            Assert.Fail(err.Message + ", " + details)

    [<TestMethod>]
    member this.WebhookResponseTest () =
        let test =
            """{
                "Type": "TransactionSettled",
                "Version": 6,
                "Payload": {},
                "Nonce": 123456789
            }""" |> ClearBankWebhooks.parsePaymentsCall

        Assert.AreEqual(123456789L, test.Nonce)

        let thisRequest = new System.Net.Http.HttpRequestMessage()
        let response = ClearBankWebhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName thisRequest test.Nonce |> Async.RunSynchronously

        Assert.IsNotNull response


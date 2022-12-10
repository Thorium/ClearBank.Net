namespace ClearBankTests

open ClearBank
open System
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type TestClass () =
    let rnd = new System.Random()
    let logging(status,content) =
        match parseClearBankErrorContent content with
        | ClearBankEmptyResponse -> Console.WriteLine "Response was empty"
        | ClearBankTransactionError errors -> errors |> Seq.iter(fun (tid,err) -> Console.WriteLine("Transaction id " + tid + " failed for " + err))
        | ClearBankGeneralError(title, detail) -> Console.WriteLine(title + ", " + detail)
        | ClearBankUnknownError content -> Console.WriteLine("JSON: " + content)

    let clearbankDefaultConfig =
        {
            BaseUrl = "https://institution-api-sim.clearbank.co.uk/"
            PrivateKey = TestParameters.clearbankPrivateKey
            AzureKeyVaultName = TestParameters.azureKeyVaultName
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

    let azureKeyVaultCertificateName = TestParameters.azureKeyVaultCertificateName
    let AssertTestResult actual =
        match actual with
        | Ok _ -> Assert.IsTrue true
        | Error (err:Exception,details) ->
            Assert.Fail(err.Message + ", " + details)

    [<TestMethod>]
    member this.TestMethodPassingTest () =
        task {
            let! actual = callTestEndpoint clearbankDefaultConfig azureKeyVaultCertificateName 
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.ProcessPaymentsTest () =
        task {
            let expected = Ok ()

            let target1 = 
                {
                    To = UK_Domestic("20-20-15", "55555555")
                    AccountHolder = "Mr Test"
                    Sum = 123.00m
                    Currency = "GBP"
                    Description = "Phone Bill"
                    PaymentReference = "123456789" + rnd.Next(1000).ToString()
                    TransactionId = "123456789" + rnd.Next(10000).ToString()
                } |> ClearBank.createCreditTransfer

            let target2 = 
                {
                    To = UK_Domestic("40-47-84", "70872490")
                    AccountHolder = "John Doe"
                    Sum = 123.00m
                    Currency = "GBP"
                    Description = "Some money"
                    PaymentReference = "12345" + rnd.Next(1000).ToString()
                    TransactionId = "12345" + rnd.Next(10000).ToString() // End-to-end: You identify corresponding webhooks with this.
                } |> ClearBank.createCreditTransfer

            let xreq = Guid.NewGuid()
            let batchId = "Batch123" + rnd.Next(1000).ToString()
            let instructions = ClearBank.createPaymentInstruction "1 Test Street, Teston TE57 1NG" None batchId TestParameters.transferFromAccount [| target1; target2 |]
            let! actual = ClearBank.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xreq [| instructions |]
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.CreateAccountTest () =
        task {
            let sortcode = match TestParameters.transferFromAccount with UK_Domestic(s, _) -> s | _ -> "04-06-05"
            let! actual = ClearBank.createNewAccount clearbankDefaultConfig azureKeyVaultCertificateName (Guid.NewGuid()) sortcode "Test account" "Mr Account Tester"
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.GetAccountsTest () =
        task {
            let! actual = ClearBank.getAccounts clearbankDefaultConfig
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
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.GetTransactionsTest () =
        task {
            let! actual = ClearBank.getTransactions clearbankDefaultConfig (Some 1000) None None None
            match actual with
            | Ok x ->
                let transactions =
                    x.Transactions
                    |> Array.map (fun t ->
                         t.TransactionTime.ToString() + " " + t.EndToEndIdentifier + ": " +
                         t.CounterpartAccount.Identification.SortCode  + " " + t.CounterpartAccount.Identification.AccountNumber  + " " +
                            t.Amount.InstructedAmount.ToString("F") + " " + t.Amount.Currency + ", " + t.TransactionReference)
                let str = String.Join("\r\n", transactions)
                Assert.AreNotEqual(0, transactions.Length)
                Assert.AreNotEqual("",str)
 
            | Error (err:Exception,details) ->
                Assert.Fail(err.Message + ", " + details)
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.WebhookResponseTest () =
        task {
            let test =
                """{
                    "Type": "TransactionSettled",
                    "Version": 6,
                    "Payload": {},
                    "Nonce": 123456789
                }""" |> ClearBankWebhooks.parsePaymentsCall

            Assert.AreEqual(123456789L, test.Nonce)

            let thisRequest = new System.Net.Http.HttpRequestMessage()
            let! response = ClearBankWebhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName thisRequest test.Nonce

            Assert.IsNotNull response
        } :> System.Threading.Tasks.Task

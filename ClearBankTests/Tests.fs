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
            BaseUrl = TestParameters.clearbankUri
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
        | Ok res ->

            Assert.IsTrue true
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
                    Sum = 123.10m
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
            let! actual = ClearBank.createNewAccount clearbankDefaultConfig azureKeyVaultCertificateName (Guid.NewGuid()) TestParameters.sortCode "Test account" "Mr Account Tester" 
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

            let nonce = test.Nonce
            Assert.AreEqual(123456789L, nonce, 0L)

            let thisRequest = new System.Net.Http.HttpRequestMessage()
            let! response = ClearBankWebhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName thisRequest test.Nonce

            Assert.IsNotNull response
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.VerifyWebhookTest () =
        task {
            let publicKeyXml = "<RSAKeyValue><Modulus>v71mKsJJhpfBPluwl2+1ZfGLNtE2EZWyf2UkwF/QGJddycsFoKVpKZZP+LLmrNLZXKJWd7k2tcj/jwKZEbIjpBOMzCTLmiTXNr8aBwgb7FhUX9AQ62jDKvRW7jUTFPkzDTOuLto02iDSUCLGSGpico1MM0uS0NgY9oy9pMZGISBulOXAZ/aFABqpzRsId+JGgHCCPJm/HF6uAp/rbF78VHnzA2GvNUrUXBm0vGiX/JPIc/xhItRpT7IcAM7/RAy6e7kKxak60FK7rQkXTrcXlD/u34644Tuip3Th+9IzALIUahijWJOnO5bSo5CG4jk/qke2m8egkj1ojDO4gxS54JWIdL1SpB6adFoyDYD5FNrnwMmRklSel/sb1hjHPkU+zex8t+i//meC8kOXPh/R65xbOXZlPIEqFz4+M6QSAGQCtAa5GRqiz2vAkcxHQHW07VLYRFUbRYlw4ju4w2PRM7ur+X0iMqdJiBQX6hMJIhiDMWXZvL3XwOooz7D4bk99vIliJ1mB821uER2oRV5FBJhdDq5VfAfXRrZwCrbo8HacTMw9NrN32vN9HGJi7bfm/y8FD9TQnsSV01dfMKayO3K1GbIx54bTy5wufv/n3kd4c2hkga9jRfa2HEFTSkLPkPoHLD8/NRs5j6a5Ua8/qXRJbFQIXhYAme9THhSiUcs=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
            let signature = System.Convert.FromBase64String "hGbwi7tYImp9myQbrhxhLGLlMVV+qoCXlkTrbnn8zhTebyfjcDDWgYZq+WDVnS9323EjVfIuwXN2CMrFnad+EYs2gMAg7deVS+eCTx+xA5hRYeFoDpfR4qR6aj3X14h1Oe8BgHbRL1O938D0qKsNKA1/sxX6+x2fhYcz/svqSddYqhbB3xb7HIeZz+0G10TG4XpnTw+WB9j2jhO2mDQqJikBwloqYtq3mx0V+fXR3EUfdKK3pryLVqXEB0tMwgqp5WUvkL1w8dsd57VFxdWZi62HRQB9c2cArORJmmdpwVkodEiW1l6JHsJECOq0mxKmeo/LxRzMWofbG3TnwW4i7GbOSUy7uZ6aq81s0z/ToeSF53Y0gSueLGib2itG6Iz74M5rmZgih5cIHBfS62M73uIncaY60NiDzkSR3YwZxoN+Dz85B+z86VzRjqKqIV49goIWlhXM8b+GPTwF0DbzbDfkPlPIgcXBM9D/oCg1DdZlZk4C9gky7S9xwbgUE76+N8Slec8J8r9IBPJMgJV80qmF8AwqBEpAe1EZmFJAxGTiqjMIqB26jof6UcqWN3S2nZ77l2P5ZiihvSQXLGFERGapfHNSsLdHZk2+j+dCt22HtCM4guH3yudhIKH1rmVv5NVcemTa9caxHdAz0pkZFTQuP88G/oLuA89DB0XEXv4="
            let requestBody = """{"Type":"FITestEvent","Version":1,"Payload":"Test","Nonce":1125446983}"""
            let! isVerified = ClearBank.verifySignature publicKeyXml signature requestBody

            Assert.IsNotNull isVerified
            Assert.IsTrue isVerified
        } :> System.Threading.Tasks.Task

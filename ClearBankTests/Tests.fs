namespace ClearBankTests

open ClearBank
open System
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type TestClass () =

    let clearbankDefaultConfig =
        {
            BaseUrl = "https://institution-api-sim.clearbank.co.uk/"
            PrivateKey = "..."
            AzureKeyVaultName =  "myVault"
            AzureKeyVaultCertificateName = "my-cert"
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
        } : ClearbankConfiguration

    let AssertTestResult actual =
        match actual with
        | Ok _ -> Assert.IsTrue true
        | Error (err:Exception,details) ->
            Assert.Fail(err.Message + ", " + details)

    [<TestMethod>]
    member this.TestMethodPassingTest () =
        let actual = callTestEndpoint clearbankDefaultConfig |> Async.RunSynchronously
        AssertTestResult actual

    [<TestMethod>]
    member this.ProcessPaymentsTest () =

        let expected = Ok ()
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
        let actual = ClearBank.callClearbank clearbankDefaultConfig (Guid.NewGuid()) [| instructions |] |> Async.RunSynchronously
        AssertTestResult actual


    [<TestMethod>]
    member this.CreateAccountTest () =
        let actual = ClearBank.createNewAccount clearbankDefaultConfig (Guid.NewGuid()) "04-06-98" "Test account" None |> Async.RunSynchronously
        AssertTestResult actual

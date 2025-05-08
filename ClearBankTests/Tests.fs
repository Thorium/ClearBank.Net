namespace ClearBankTests

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

[<AutoOpen>]
module TestHelpers =

    open ClearBank.Common

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

[<TestClass>]
type ``UK Tests`` () =

    [<TestMethod>]
    member this.TestMethodPassingTest () =
        task {
            let! actual = ClearBank.UK.callTestEndpoint clearbankDefaultConfig azureKeyVaultCertificateName 
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.ProcessPaymentsTest () =
        task {
            let expected = Ok ()

            let target1 =
                ClearBank.UK.createCreditTransfer
                    {
                        To = ClearBank.Common.UK_Domestic("20-20-15", "55555555")
                        AccountHolder = "Mr Test"
                        Sum = 123.10m
                        Currency = "GBP"
                        Description = "Phone Bill"
                        PaymentReference = "123456789" + rnd.Next(1000).ToString()
                        TransactionId = "123456789" + rnd.Next(10000).ToString()
                    }

            let target2 = 
                ClearBank.UK.createCreditTransfer
                    {
                        To = ClearBank.Common.UK_Domestic("40-47-84", "70872490")
                        AccountHolder = "John Doe"
                        Sum = 123.00m
                        Currency = "GBP"
                        Description = "Some money"
                        PaymentReference = "12345" + rnd.Next(1000).ToString()
                        TransactionId = "12345" + rnd.Next(10000).ToString() // End-to-end: You identify corresponding webhooks with this.
                    }

            let xreq = Guid.NewGuid()
            let batchId = "Batch123" + rnd.Next(1000).ToString()
            let instructions = ClearBank.UK.createPaymentInstruction "1 Test Street, Teston TE57 1NG" None batchId TestParameters.transferFromAccount [| target1; target2 |]
            let! actual = ClearBank.UK.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xreq [| instructions |]
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.CreateAccountTest () =
        task {
            let! actual = ClearBank.UK.createNewAccount clearbankDefaultConfig azureKeyVaultCertificateName (Guid.NewGuid()) TestParameters.sortCode "Test account" "Mr Account Tester" 
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.GetAccountsTest () =
        task {
            let! actual = ClearBank.UK.getAccounts clearbankDefaultConfig
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
            let! actual = ClearBank.UK.getTransactions clearbankDefaultConfig (Some 1000) None None None
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
                }""" |> ClearBank.Webhooks.parsePaymentsCallUK

            let nonce = test.Nonce
            Assert.AreEqual(123456789L, nonce, 0L)

            let thisRequest = new System.Net.Http.HttpRequestMessage()
            let! response = ClearBank.Webhooks.createResponse clearbankDefaultConfig azureKeyVaultCertificateName thisRequest test.Nonce

            Assert.IsNotNull response
        } :> System.Threading.Tasks.Task

    [<TestMethod>]
    member this.VerifyWebhookTest () =
        task {
            let publicKeyXml = "<RSAKeyValue><Modulus>v71mKsJJhpfBPluwl2+1ZfGLNtE2EZWyf2UkwF/QGJddycsFoKVpKZZP+LLmrNLZXKJWd7k2tcj/jwKZEbIjpBOMzCTLmiTXNr8aBwgb7FhUX9AQ62jDKvRW7jUTFPkzDTOuLto02iDSUCLGSGpico1MM0uS0NgY9oy9pMZGISBulOXAZ/aFABqpzRsId+JGgHCCPJm/HF6uAp/rbF78VHnzA2GvNUrUXBm0vGiX/JPIc/xhItRpT7IcAM7/RAy6e7kKxak60FK7rQkXTrcXlD/u34644Tuip3Th+9IzALIUahijWJOnO5bSo5CG4jk/qke2m8egkj1ojDO4gxS54JWIdL1SpB6adFoyDYD5FNrnwMmRklSel/sb1hjHPkU+zex8t+i//meC8kOXPh/R65xbOXZlPIEqFz4+M6QSAGQCtAa5GRqiz2vAkcxHQHW07VLYRFUbRYlw4ju4w2PRM7ur+X0iMqdJiBQX6hMJIhiDMWXZvL3XwOooz7D4bk99vIliJ1mB821uER2oRV5FBJhdDq5VfAfXRrZwCrbo8HacTMw9NrN32vN9HGJi7bfm/y8FD9TQnsSV01dfMKayO3K1GbIx54bTy5wufv/n3kd4c2hkga9jRfa2HEFTSkLPkPoHLD8/NRs5j6a5Ua8/qXRJbFQIXhYAme9THhSiUcs=</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"
            let signature = System.Convert.FromBase64String "hGbwi7tYImp9myQbrhxhLGLlMVV+qoCXlkTrbnn8zhTebyfjcDDWgYZq+WDVnS9323EjVfIuwXN2CMrFnad+EYs2gMAg7deVS+eCTx+xA5hRYeFoDpfR4qR6aj3X14h1Oe8BgHbRL1O938D0qKsNKA1/sxX6+x2fhYcz/svqSddYqhbB3xb7HIeZz+0G10TG4XpnTw+WB9j2jhO2mDQqJikBwloqYtq3mx0V+fXR3EUfdKK3pryLVqXEB0tMwgqp5WUvkL1w8dsd57VFxdWZi62HRQB9c2cArORJmmdpwVkodEiW1l6JHsJECOq0mxKmeo/LxRzMWofbG3TnwW4i7GbOSUy7uZ6aq81s0z/ToeSF53Y0gSueLGib2itG6Iz74M5rmZgih5cIHBfS62M73uIncaY60NiDzkSR3YwZxoN+Dz85B+z86VzRjqKqIV49goIWlhXM8b+GPTwF0DbzbDfkPlPIgcXBM9D/oCg1DdZlZk4C9gky7S9xwbgUE76+N8Slec8J8r9IBPJMgJV80qmF8AwqBEpAe1EZmFJAxGTiqjMIqB26jof6UcqWN3S2nZ77l2P5ZiihvSQXLGFERGapfHNSsLdHZk2+j+dCt22HtCM4guH3yudhIKH1rmVv5NVcemTa9caxHdAz0pkZFTQuP88G/oLuA89DB0XEXv4="
            let requestBody = """{"Type":"FITestEvent","Version":1,"Payload":"Test","Nonce":1125446983}"""
            let! isVerified = ClearBank.Common.verifySignature publicKeyXml signature requestBody

            Assert.IsNotNull isVerified
            Assert.IsTrue isVerified
        } :> System.Threading.Tasks.Task

type UKM = ClearBank.UK.MultiCurrency.MccyPaymentsV1

[<TestClass>]
type ``UK MultiCurrency Tests`` () =

    [<TestMethod>]
    member this.GetAccountsTest () =
        task {
            let! actual = ClearBank.UK.MultiCurrency.getAccounts clearbankDefaultConfig
            match actual with
            | Ok x ->
                let accounts =
                    x.Accounts
                    |> Array.map (fun a ->
                        $"{a.Name} %A{a.Currencies} status {a.Status}: {a.StatusInformation}")

                Assert.AreNotEqual(0, accounts.Length)
                Assert.AreNotEqual("",accounts.[0])
                Assert.AreNotEqual("",String.Join("\r\n", accounts))
 
            | Error (err:Exception,details) ->
                if err.Message = "One or more errors occurred. (Response status code does not indicate success: 404 (Not Found).)" then
                    // Might be an error, or might be that no multi-currency accounts have been created!
                    ()
                else Assert.Fail(err.Message + ", " + details)
        } :> System.Threading.Tasks.Task


    [<TestMethod; Ignore("RoutingCode 010203 from GB was not found")>]
    member this.CreateAccountTest () =
        task {
            let sortCode = "01-02-03" // note: You need a multi-currency sort-code, see: https://clearbank.github.io/uk/docs/multi-currency/multi-currency-account-types
            let! actual = ClearBank.UK.MultiCurrency.createNewAccount clearbankDefaultConfig azureKeyVaultCertificateName (Guid.NewGuid()) sortCode "Test currency account" "Mr Account Tester"
                                                           ClearBank.UK.MultiCurrency.AccountKind.GeneralSegregated [|"EUR"; "USD"|] Array.empty None None

            AssertTestResult actual
        } :> System.Threading.Tasks.Task

    [<TestMethod; Ignore("Not a valid account id")>]
    member this.ProcessPaymentsTest () =
        task {

            let expected = Ok ()

            let currency =  "EUR" // ClearBank.MultiCurrency.ISOCurrencySymbols()
            let creditorCountry = "FI"
            let deptorCountry = "FI"

            let xreq = Guid.NewGuid()
            let batchId = Some (Guid.NewGuid())

            let creditorAccount = ClearBank.Common.BankAccount.UK_Domestic("20-20-15", "55555555")
            let account = ClearBank.Common.BankAccount.UK_Domestic("20-20-15", "55555555")

            let creditorIban, creditorAccountnumber, creditorScheme, creditorInstitutionScheme, creditorPrivateScheme, ultimateInstitutionScheme, ultimatePrivateScheme =
                match creditorAccount with
                | ClearBank.Common.BankAccount.IBAN x -> x, null, UKM.Creditor_SchemeName(null, "IBAN"), UKM.Creditor_Identification_OrganisationIdentification_Other_SchemeName(null, "IBAN"), UKM.Creditor_Identification_PrivateIdentification_Other_SchemeName(null, "IBAN"), UKM.UltimateCreditor_Identification_OrganisationIdentification_Other_SchemeName(null, "IBAN"), UKM.UltimateCreditor_Identification_PrivateIdentification_Other_SchemeName(null, "IBAN")
                | ClearBank.Common.BankAccount.BBAN x -> null, x, UKM.Creditor_SchemeName(null, "BBAN"), UKM.Creditor_Identification_OrganisationIdentification_Other_SchemeName(null, "BBAN"), UKM.Creditor_Identification_PrivateIdentification_Other_SchemeName(null, "BBAN"), UKM.UltimateCreditor_Identification_OrganisationIdentification_Other_SchemeName(null, "BBAN"), UKM.UltimateCreditor_Identification_PrivateIdentification_Other_SchemeName(null, "BBAN")
                | ClearBank.Common.BankAccount.UK_Domestic(x, y) -> null, x.Replace("-", "").Replace(" ", "") + y, UKM.Creditor_SchemeName(null, "PRTY_COUNTRY_SPECIFIC"), UKM.Creditor_Identification_OrganisationIdentification_Other_SchemeName(null, "PRTY_COUNTRY_SPECIFIC"), UKM.Creditor_Identification_PrivateIdentification_Other_SchemeName(null, "PRTY_COUNTRY_SPECIFIC"), UKM.UltimateCreditor_Identification_OrganisationIdentification_Other_SchemeName(null, "PRTY_COUNTRY_SPECIFIC"), UKM.UltimateCreditor_Identification_PrivateIdentification_Other_SchemeName(null, "PRTY_COUNTRY_SPECIFIC")

            let accountid, deptorPrivateScheme = 
                match account with
                | ClearBank.Common.BankAccount.IBAN x -> UKM.AccountIdentifier("Iban", x), UKM.PaymentRequestItem_DebtorPrivateIdentification_Other_SchemeName(null, "IBAN")
                | ClearBank.Common.BankAccount.BBAN x -> UKM.AccountIdentifier("AccountId", x), UKM.PaymentRequestItem_DebtorPrivateIdentification_Other_SchemeName(null, "BBAN")
                | ClearBank.Common.BankAccount.UK_Domestic(x, y) -> UKM.AccountIdentifier("AccountId", x.Replace("-", "").Replace(" ", "") + y), UKM.PaymentRequestItem_DebtorPrivateIdentification_Other_SchemeName(null, "PRTY_COUNTRY_SPECIFIC")

            let creditorId =
                    //UKM.Creditor_Identification(
                    //    UKM.Creditor_Identification_OrganisationIdentification(
                    //        UKM.Creditor_Identification_OrganisationIdentification_Other(
                    //            "identification", creditorInstitutionScheme, "issuer")
                    //    ),
                    //    UKM.Creditor_Identification_PrivateIdentification(
                    //        UKM.Creditor_Identification_PrivateIdentification_DateAndPlaceOfBirth( (Some (DateTimeOffset(DateTime(1970,01,01)))), "Kuopio", creditorCountry),
                    //        UKM.Creditor_Identification_PrivateIdentification_Other(
                    //            "identification", creditorPrivateScheme
                    //        )
                    //    )
                    //),
                    null

            let creditor = 
                UKM.Creditor(
                    "John",
                    UKM.Creditor_Address(
                        "Street 1", "Street 2", "77000", creditorCountry, "Line 3"
                    ),
                    "NDEAFIHH", // BIC
                    creditorId,
                    creditorCountry, // country of residence
                    UKM.Creditor_ContactDetails("John", "john@mailinator.com"),
                    creditorIban, creditorAccountnumber,
                    creditorScheme
                )

            let ultimateCreditor =
                //UKM.UltimateCreditor(
                //        "John",
                //        UKM.UltimateCreditor_Address(creditorCountry, "Street 1", "Street 2", "Street 3", "77000"),
                //        "NDEAFIHH", // BIC
                //        UKM.UltimateCreditor_Identification(
                //            UKM.UltimateCreditor_Identification_OrganisationIdentification(
                //                UKM.UltimateCreditor_Identification_OrganisationIdentification_Other(
                //                    "identification", ultimateInstitutionScheme, "issuer"
                //                )
                //            ),
                //            UKM.UltimateCreditor_Identification_PrivateIdentification(
                //                UKM.UltimateCreditor_Identification_PrivateIdentification_DateAndPlaceOfBirth( (Some (DateTimeOffset(DateTime(1970,01,01)))), "Kuopio", creditorCountry),
                //                UKM.UltimateCreditor_Identification_PrivateIdentification_Other(
                //                    "identification", ultimatePrivateScheme
                //                )

                //            )
                //        )
                //    )
                null

            let deptorPrivateId =
                //UKM.PaymentRequestItem_DebtorPrivateIdentification(
                //        UKM.PaymentRequestItem_DebtorPrivateIdentification_DateAndPlaceOfBirth( (Some (DateTimeOffset(DateTime(1970,01,01)))), "Kuopio", deptorCountry),
                //        UKM.PaymentRequestItem_DebtorPrivateIdentification_Other(
                //            "identification", deptorPrivateScheme
                //            )
                //    )
                null

            let creditorAgent =
                UKM.CreditorAgent(
                    UKM.CreditorAgent_FinancialInstitutionIdentification(
                        "Some bank name",
                        UKM.CreditorAgent_FinancialInstitutionIdentification_AddressDetails(
                            deptorCountry, "Street 1", "Street 2", "Street 3", "77000"
                        ),
                        "NDEAFIHH", // BIC
                        null, // ABA
                        null, // clearing system id code "12345"
                        null // memberId
                    ),
                    "Branch id"
                )

            let intermediaryAgent =
                //UKM.IntermediaryAgent(
                //    UKM.IntermediaryAgent_FinancialInstitutionIdentification(
                //        UKM.IntermediaryAgent_FinancialInstitutionIdentification_AddressDetails(deptorCountry, "Street 1", "Street 2", "Street 3", "77000"),
                //        "NDEAFIHH", // BIC
                //        null, // ABA
                //        "name")
                //    )
                null

            let instructions =
                UKM.PaymentRequestItem(
                    ("123456789" + rnd.Next(10000).ToString()), // endToEndId
                    ("123456789" + rnd.Next(1000).ToString()), // paymentReference
                    Convert.ToSingle(123.00m), //sum
                    creditor,
                    "Jim Doe", //deptor name
                    UKM.PaymentRequestItem_DebtorAddress(
                        "Street 1", "Street 2", "77000", deptorCountry, "Line 3"
                    ),
                    accountid,
                    currency,
                    "NDEAFIHH", //Deptor BIC
                    deptorPrivateId,
                    intermediaryAgent,
                    creditorAgent,
                    "instructionsForAgent",
                    UKM.Purpose("code", "proprietary"),
                    UKM.RemittanceInformation("Additional info"),
                    ultimateCreditor)

            
            let! actual = ClearBank.UK.MultiCurrency.transferPayments clearbankDefaultConfig azureKeyVaultCertificateName xreq batchId currency [| instructions |]
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

[<TestClass>]
type ``EU Tests`` () =

    [<TestMethod; Ignore("Not tested yet")>]
    member this.ProcessSepaPaymentsTest () =
        task {

            let xreq = Guid.NewGuid()

            let payment =
                ClearBank.EU.SepaV1.CreateSepaOutboundPaymentRequest(
                    "12345" + rnd.Next(10000).ToString(), //End-to-end id
                    Convert.ToDouble(12.10m), //payment sum
                    "EUR", //currency
                    ClearBank.EU.SepaV1.Debtor(
                        "John Doe", //name
                        "GB15HBUK40127612345678", // IBAN
                        ClearBank.EU.SepaV1.PostalAddress("Lahti", "FI", "Hameentie", "12", "15000", ""),
                        null //ClearBank.EU.SepaV1.Identification(...)
                    ),
                    ClearBank.EU.SepaV1.Creditor(
                        "John Doe Jr", // Name
                        "GB15HBUK40127612345678", // IBAN
                        ClearBank.EU.SepaV1.PostalAddress("Tampere", "FI", "Hameenkatu", "5", "33700", ""),
                        null //ClearBank.EU.SepaV1.Identification(...)

                    ),
                    ClearBank.EU.SepaV1.CreditorAgent(
                        "NDEAFIHH" // BIC
                    ),
                    "Additional info" //remittance information
                )

            let! actual = ClearBank.EU.sepaTransferPayments clearbankDefaultConfig azureKeyVaultCertificateName xreq payment
            AssertTestResult actual
        } :> System.Threading.Tasks.Task

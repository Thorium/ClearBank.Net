namespace ClearBankTests

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open ClearBank
open ClearBank.Common
open ClearBank.UK

/// A request as the fake handler saw it.
type CapturedRequest =
    { Method: string
      PathAndQuery: string
      Headers: Map<string, string>
      Body: string }

/// Answers every request with respond (method, path) and records it.
type FakeClearBank(respond: string -> string -> HttpStatusCode * string) =
    inherit HttpMessageHandler()
    let calls = Collections.Concurrent.ConcurrentQueue<CapturedRequest>()

    member _.Calls = calls |> Seq.toList

    override _.SendAsync(request, _) =
        task {
            let! body =
                if isNull request.Content then Task.FromResult ""
                else request.Content.ReadAsStringAsync()

            let headers =
                request.Headers
                |> Seq.map (fun h -> h.Key, String.Join(",", h.Value))
                |> Map.ofSeq

            calls.Enqueue
                { Method = request.Method.Method
                  PathAndQuery = request.RequestUri.PathAndQuery
                  Headers = headers
                  Body = body }

            let status, content = respond request.Method.Method request.RequestUri.AbsolutePath
            return new HttpResponseMessage(status, Content = new StringContent(content, Text.Encoding.UTF8, "application/json"))
        }

[<AutoOpen>]
module ClientTestHelpers =

    let rsaKey = lazy (RSA.Create 2048)
    let publicKeyXml () = rsaKey.Value.ToXmlString false
    let signer () = RequestSigner.rsa rsaKey.Value
    let ct = CancellationToken.None

    let equal (expected: 'T, actual: 'T) =
        if expected <> actual then
            raise (AssertFailedException $"Expected %A{expected}, got %A{actual}")

    let clientWith (handler: FakeClearBank) =
        let http = new HttpClient(handler, BaseAddress = Uri "https://institution-api-sim.clearbank.co.uk/")
        ClearBankClient.create http "api-token" (signer ())

    let expectOk =
        function
        | Ok value -> value
        | Error(err: exn, details: string) -> failwith $"{err.Message}: {details}"

    /// The check verifySignature makes, synchronously.
    let verifies (call: CapturedRequest) =
        KeyVault.verifyPublic (publicKeyXml ()) (Convert.FromBase64String call.Headers["DigitalSignature"]) call.Body

[<TestClass>]
type ``Signer Tests`` () =

    [<TestMethod>]
    member _.``a local RSA signature verifies with verifySignature`` () : Task =
        task {
            let body = """{"Type":"FITestEvent","Version":1,"Payload":"Test","Nonce":1125446983}"""
            let! signature = (signer ()).Sign body ct
            let! verified = verifySignature (publicKeyXml ()) signature body
            Assert.IsTrue verified
            let! tampered = verifySignature (publicKeyXml ()) signature (body.Replace("Test", "Tset"))
            Assert.IsFalse tampered
        }

    [<TestMethod>]
    member _.``a digest signer such as AWS KMS gives the same PKCS#1 signature`` () : Task =
        task {
            let body = """{"amount":12.50,"currency":"GBP"}"""
            let kms = RequestSigner.ofDigest (fun digest _ -> Task.FromResult(rsaKey.Value.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)))
            let! fromDigest = kms.Sign body ct
            let! direct = (signer ()).Sign body ct
            CollectionAssert.AreEqual(direct, fromDigest)
            let! verified = verifySignature (publicKeyXml ()) fromDigest body
            Assert.IsTrue verified
        }

    [<TestMethod>]
    member _.``the C# signer factories wrap the same functions`` () : Task =
        task {
            let fromFunc = RequestSigner.Create(Func<string, CancellationToken, Task<byte[]>>(fun body _ -> Task.FromResult(Text.Encoding.UTF8.GetBytes body)))
            let! echoed = fromFunc.Sign "abc" ct
            equal("abc", Text.Encoding.UTF8.GetString echoed)
            let! viaRsa = (RequestSigner.FromRsa rsaKey.Value).Sign "abc" ct
            let! verified = verifySignature (publicKeyXml ()) viaRsa "abc"
            Assert.IsTrue verified
        }

    [<TestMethod>]
    member _.``a signed webhook response carries a verifiable signature over its body`` () : Task =
        task {
            use request = new HttpRequestMessage()
            use! response = Webhooks.createSignedResponseAsync (signer ()) request 123456789L ct
            let! body = response.Content.ReadAsStringAsync()
            equal("""{"Nonce":123456789}""", body)
            let signature = response.Headers.GetValues "DigitalSignature" |> Seq.head |> Convert.FromBase64String
            let! verified = verifySignature (publicKeyXml ()) signature body
            Assert.IsTrue verified
        }

[<TestClass>]
type ``Client Tests`` () =

    [<TestMethod>]
    member _.``a client needs an HttpClient with a BaseAddress`` () =
        use http = new HttpClient()
        Assert.ThrowsException<ArgumentException>(fun () -> ClearBankClient.create http "token" (signer ()) |> ignore) |> ignore

    [<TestMethod>]
    member _.``an account update PATCHes the documented fields, signed`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let client = clientWith handler
            let requestId, accountId = Guid.NewGuid(), Guid.NewGuid()

            let update =
                { AccountUpdate.empty with
                    Status = ValueSome AccountStatus.Suspended
                    StatusReason = ValueSome AccountStatusReason.KYCRequired
                    LegalOwnerType = ValueSome LegalOwnerType.Business }

            let! result = Api.updateAccountAsync client requestId accountId update ct
            expectOk result
            let call = handler.Calls |> List.exactlyOne
            equal("PATCH", call.Method)
            equal($"/v1/accounts/{accountId:D}", call.PathAndQuery)
            equal("Bearer api-token", call.Headers["Authorization"])
            equal(requestId.ToString "N", call.Headers["X-Request-ID"])
            equal("""{"status":"Suspended","statusReason":"KYCRequired","legalOwnerType":"Business"}""", call.Body)
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``closing an account sets Closed with the reason`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let! result = Api.closeAccountAsync (clientWith handler) (Guid.NewGuid()) (Guid.NewGuid()) AccountStatusReason.AccountSwitched ct
            expectOk result
            equal("""{"status":"Closed","statusReason":"AccountSwitched"}""", (handler.Calls |> List.exactlyOne).Body)
        }

    [<TestMethod>]
    member _.``calls share the caller's HttpClient`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let client = clientWith handler
            let! first = Api.updateAccountAsync client (Guid.NewGuid()) (Guid.NewGuid()) { AccountUpdate.empty with OwnerName = ValueSome "A" } ct
            let! second = Api.updateVirtualAccountAsync client (Guid.NewGuid()) (Guid.NewGuid()) (Guid.NewGuid()) { VirtualAccountUpdate.empty with OwnerName = ValueSome "B" } ct
            expectOk first
            expectOk second
            equal(2, handler.Calls.Length)
            // Still usable: the library never disposes a caller's HttpClient.
            let! third = Api.updateAccountAsync client (Guid.NewGuid()) (Guid.NewGuid()) AccountUpdate.empty ct
            expectOk third
        }

    [<TestMethod>]
    member _.``a refused request is an Error carrying the response body`` () : Task =
        task {
            let problem = """{"title":"Bad Request","detail":"statusReason is not allowed when enabling","status":400}"""
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.BadRequest, problem)
            match! Api.updateAccountAsync (clientWith handler) (Guid.NewGuid()) (Guid.NewGuid()) AccountUpdate.empty ct with
            | Ok _ -> Assert.Fail "expected an error"
            | Error(_, details) ->
                equal(problem, details)

                match parseClearBankErrorContent details with
                | ClearBankGeneralError(title, _) -> equal("Bad Request", title)
                | other -> Assert.Fail $"%A{other}"

            match! Api.setCopParticipationAsync (clientWith handler) (Guid.NewGuid()) (AccountRef.Real(Guid.NewGuid())) CopParticipation.Participate ct with
            | Ok _ -> Assert.Fail "expected an error"
            | Error(err, details) ->
                equal(problem, details)
                Assert.IsInstanceOfType(err, typeof<ClearBankResponseException>)
                equal(HttpStatusCode.BadRequest, (err :?> ClearBankResponseException).Status)
        }

    [<TestMethod>]
    member _.``a generated-client failure is an Error carrying the response body`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NotFound, """{"title":"Not Found","detail":"No such account"}""")
            match! Api.getAccountAsync (clientWith handler) (Guid.NewGuid()) ct with
            | Ok _ -> Assert.Fail "expected an error"
            | Error(_, details) -> StringAssert.Contains(details, "No such account")
        }

    [<TestMethod>]
    member _.``a cancelled token cancels instead of returning an Error`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            use cancelled = new CancellationTokenSource()
            do! cancelled.CancelAsync()

            let! thrown =
                task {
                    try
                        let! _ = Api.updateAccountAsync (clientWith handler) (Guid.NewGuid()) (Guid.NewGuid()) AccountUpdate.empty cancelled.Token
                        return false
                    with :? OperationCanceledException ->
                        return true
                }

            Assert.IsTrue thrown
        }

    [<TestMethod>]
    member _.``generated clients run concurrently on one HttpClient and omit absent filters`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"accounts":[],"transactions":[],"halLinks":[]}""")
            let client = clientWith handler

            let outcome (call: Task<Result<_, exn * string>>) =
                task {
                    match! call with
                    | Ok _ -> return "ok"
                    | Error(err, details) -> return $"{err.Message} {details}"
                }

            let! outcomes =
                [ Api.getAccountsAsync client ValueNone ValueNone ct |> outcome
                  Api.getTransactionsAsync client ValueNone (ValueSome 5) ValueNone ValueNone ct |> outcome
                  Api.getUnpaidDirectDebitsAsync client "04-06-05" BacsItemQuery.empty ValueNone ValueNone ct |> outcome
                  Api.getUnpaidCollectionsAsync client (AccountRef.Real(Guid.NewGuid())) ValueNone ValueNone ValueNone ValueNone ValueNone ValueNone ct |> outcome ]
                |> Task.WhenAll

            CollectionAssert.AreEqual([| "ok"; "ok"; "ok"; "ok" |], outcomes)
            let paths = handler.Calls |> List.map _.PathAndQuery

            CollectionAssert.Contains(Collections.Generic.List paths, "/v2/Transactions?pageSize=5")
            CollectionAssert.Contains(Collections.Generic.List paths, "/v1/Transactions/040605/DirectDebit/Unpaid")
            Assert.IsTrue(paths |> List.forall (fun p -> not (p.Contains "=&" || p.EndsWith "=")))
        }

    [<TestMethod>]
    member _.``date filters are sent as ISO 8601 instants with their offset`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"transactions":[],"halLinks":[]}""")
            let start = DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
            let! result = Api.getTransactionsAsync (clientWith handler) ValueNone ValueNone (ValueSome start) ValueNone ct
            expectOk result |> ignore
            equal("/v2/Transactions?startDateTime=2026-10-01T00%3A00%3A00.0000000%2B00%3A00", (handler.Calls |> List.exactlyOne).PathAndQuery)
        }

    [<TestMethod>]
    member _.``reading every page goes from page 1 until one comes back empty`` () : Task =
        task {
            use handler =
                new FakeClearBank(fun _ _ ->
                    HttpStatusCode.OK, """{"transactions":[],"halLinks":[]}""")

            let! none = Api.getEveryTransactionAsync (clientWith handler) (ValueSome 2) ValueNone ValueNone ct
            equal(0, (expectOk none).Length)
            equal(1, handler.Calls.Length)
            StringAssert.Contains(handler.Calls.Head.PathAndQuery, "pageNumber=1")
            StringAssert.Contains(handler.Calls.Head.PathAndQuery, "pageSize=2")
        }

    [<TestMethod>]
    member _.``reading every page keeps each page's items, and fails if a page does`` () : Task =
        task {
            let mutable asked = 0

            use handler =
                new FakeClearBank(fun _ _ ->
                    asked <- asked + 1

                    match asked with
                    | 1 -> HttpStatusCode.OK, """{"transactions":[{},{}],"halLinks":[]}"""
                    | 2 -> HttpStatusCode.OK, """{"transactions":[{}],"halLinks":[]}"""
                    | _ -> HttpStatusCode.OK, """{"transactions":[],"halLinks":[]}""")

            let! all = Api.getEveryTransactionAsync (clientWith handler) (ValueSome 2) ValueNone ValueNone ct
            equal(3, (expectOk all).Length)
            equal(3, handler.Calls.Length)
            StringAssert.Contains(handler.Calls[2].PathAndQuery, "pageNumber=3")

            use failing = new FakeClearBank(fun _ _ -> HttpStatusCode.ServiceUnavailable, "")
            let! failed = Api.getEveryTransactionAsync (clientWith failing) ValueNone ValueNone ValueNone ct
            Assert.IsTrue(failed.IsError)
        }

    [<TestMethod>]
    member _.``virtual account transactions use the virtual path`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"transactions":[],"halLinks":[]}""")
            let accountId, virtualId = Guid.NewGuid(), Guid.NewGuid()
            let! result = Api.getAccountTransactionsAsync (clientWith handler) (AccountRef.Virtual(accountId, virtualId)) (ValueSome 2) (ValueSome 10) ValueNone ValueNone ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            StringAssert.StartsWith(call.PathAndQuery.ToLowerInvariant(), $"/v2/accounts/{accountId:D}/virtual/{virtualId:D}/transactions")
            StringAssert.Contains(call.PathAndQuery, "pageNumber=2")
            equal("Bearer api-token", call.Headers["Authorization"])
        }

    [<TestMethod>]
    member _.``transaction returns are signed and list each transaction with its reason`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, "{}")
            let accountId, transactionId = Guid.NewGuid(), Guid.NewGuid()
            let! result = Api.returnTransactionsAsync (clientWith handler) (Guid.NewGuid()) (AccountRef.Real accountId) [ transactionId, "0" ] ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            equal("POST", call.Method)
            StringAssert.StartsWith(call.PathAndQuery.ToLowerInvariant(), $"/v1/accounts/{accountId:D}/transactions/returns")
            StringAssert.Contains(call.Body, transactionId.ToString "D")
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``a mandate cancellation is a signed DELETE with the reason code`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, "{}")
            let accountId, virtualId, mandateId = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
            let! result = Api.cancelMandateAsync (clientWith handler) (Guid.NewGuid()) (AccountRef.Virtual(accountId, virtualId)) mandateId "1" ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            equal("DELETE", call.Method)
            StringAssert.StartsWith(call.PathAndQuery.ToLowerInvariant(), $"/v1/accounts/{accountId:D}/virtual/{virtualId:D}/mandates/{mandateId:D}")
            StringAssert.Contains(call.Body, "\"reasonCode\":\"1\"")
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``virtual accounts are requested with their external identifiers`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.Accepted, """{"halLinks":[]}""")
            let accountId = Guid.NewGuid()
            let accounts = [ { OwnerName = "Ada"; ExternalIdentifier = ValueSome "customer-1" } ]
            let! result = Api.createVirtualAccountsAsync (clientWith handler) (Guid.NewGuid()) accountId accounts ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            StringAssert.StartsWith(call.PathAndQuery.ToLowerInvariant(), $"/v2/accounts/{accountId:D}/virtual")
            StringAssert.Contains(call.Body, "customer-1")
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``Confirmation of Payee sends the padded identification and reads the report`` () : Task =
        task {
            let response =
                """{"Data":{"VerificationReport":{"Matched":false,"Name":"Ada Lovelace","ReasonCode":"MBAM","ReasonCodeDescription":"Close match","ResponseWithinSla":true}}}"""

            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, response)

            let check =
                { SortCode = "04-00-04"
                  AccountNumber = "1234567"
                  OwnerName = ",,Ada Lovelase"
                  LegalOwnerType = LegalOwnerType.Personal
                  SecondaryReference = ValueNone
                  EndToEndId = ValueSome "e2e-1" }

            let! result = Api.verifyPayeeAsync (clientWith handler) (Guid.NewGuid()) check ct
            let report = expectOk result
            Assert.IsFalse report.Matched
            equal(ValueSome "Ada Lovelace", report.Name)
            equal(ValueSome "MBAM", report.ReasonCode)
            equal(ValueSome true, report.ResponseWithinSla)

            let call = handler.Calls |> List.exactlyOne
            equal("/open-banking/outbound/v1/name-verification", call.PathAndQuery)

            equal(
                """{"SchemeName":"SortCodeAccountNumber","LegalOwnerType":"Personal","Identification":"04000401234567","OwnerName":",,Ada Lovelase","EndToEndIdentification":"e2e-1"}""",
                call.Body)

            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``a response without the report is an Error`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"unexpected":true}""")
            let check = { SortCode = "040004"; AccountNumber = "12345678"; OwnerName = ",,A"; LegalOwnerType = LegalOwnerType.Business; SecondaryReference = ValueNone; EndToEndId = ValueNone }
            match! Api.verifyPayeeAsync (clientWith handler) (Guid.NewGuid()) check ct with
            | Ok _ -> Assert.Fail "expected an error"
            | Error(err, _) -> StringAssert.Contains(err.Message, "VerificationReport")
        }

    [<TestMethod>]
    member _.``the CoP opt-out accepts a 204 without a body`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let! result = Api.setCopParticipationAsync (clientWith handler) (Guid.NewGuid()) (AccountRef.Real(Guid.NewGuid())) CopParticipation.Participate ct
            expectOk result
            equal("""{"optOut":false}""", (handler.Calls |> List.exactlyOne).Body)
        }

    [<TestMethod>]
    member _.``an account update leaves out the fields it does not change`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let! result = Api.updateAccountAsync (clientWith handler) (Guid.NewGuid()) (Guid.NewGuid()) { AccountUpdate.empty with OwnerName = ValueSome "Ada Ltd" } ct
            expectOk result
            equal("""{"ownerName":"Ada Ltd"}""", (handler.Calls |> List.exactlyOne).Body)
        }

    [<TestMethod>]
    member _.``account closure is requested with its reason and its status read back`` () : Task =
        task {
            let status =
                """{"accountClosure":{"accountStatus":"Enabled","closureStatus":"Failed","closureFailureReasons":["NominatedAccountInvalid"]},"halLinks":[]}"""

            use handler =
                new FakeClearBank(fun httpMethod _ ->
                    if httpMethod = "GET" then HttpStatusCode.OK, status
                    else HttpStatusCode.Accepted, """{"accountClosure":{"closureStatus":"Pending"},"halLinks":[]}""")

            let client = clientWith handler
            let accountId = Guid.NewGuid()
            let! requested = Api.requestAccountClosureAsync client (Guid.NewGuid()) accountId AccountStatusReason.AccountSwitched ct
            let! read = Api.getAccountClosureAsync client accountId ct
            equal(Some "Pending", (expectOk requested).AccountClosure.ClosureStatus)
            let closure = (expectOk read).AccountClosure
            equal(Some "Failed", closure.ClosureStatus)
            CollectionAssert.AreEqual([| "NominatedAccountInvalid" |], closure.ClosureFailureReasons)

            let post = handler.Calls |> List.head
            equal($"/v1/accounts/{accountId:D}/closure", post.PathAndQuery)
            equal("""{"closureReason":"AccountSwitched"}""", post.Body)
            Assert.IsTrue(verifies post)
        }

    [<TestMethod>]
    member _.``a CHAPS payment sends exact amounts, ISO codes and each account shape`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.Created, """{"paymentId":"55555555-5555-5555-5555-555555555555"}""")
            let address = ChapsV6.PartyAddressDetailsDtoV6("London", "GB", "EC3A 8EP", Some "30", None, None)
            let party name = ChapsV6.CustomerPartyDetailsDtoV6(name, address, ChapsV6.OrganisationIdentifierV6(Some "CLRBGB22", None, None), null)

            let payment =
                ChapsV6.CreateCustomerPaymentDtoV6(
                    "instr-1", "e2e-1", ChapsV6.InterBankSettlementAmountDtoV6(1234567.89, "GBP"),
                    chapsAccount (IBAN "GB33CLRB04066812345678"), chapsAccount (UK_Domestic("04-06-68", "1234567")), party "Ada Ltd",
                    chapsAccount (BBAN "CLRB04066887654321"), party "Bob Ltd",
                    ChapsV6.PurposeCodeV6.Loan, ChapsV6.CategoryPurposeCodeV6.Loan, ChapsV6.StructuredRemittanceInformationDtoV6(Some "INV-1"))

            let! result = Api.chapsCustomerPaymentAsync (clientWith handler) (Guid.NewGuid()) payment ct
            equal(Guid "55555555-5555-5555-5555-555555555555", (expectOk result).PaymentId)
            let call = handler.Calls |> List.exactlyOne
            equal("/payments/chaps/v6/customer-payments", call.PathAndQuery)
            StringAssert.Contains(call.Body, "\"amount\":1234567.89,")
            StringAssert.Contains(call.Body, "\"purpose\":\"LOAN\",\"categoryPurpose\":\"LOAN\"")
            StringAssert.Contains(call.Body, "\"sourceAccount\":{\"iban\":\"GB33CLRB04066812345678\"}")
            StringAssert.Contains(call.Body, "\"debtorAccount\":{\"schemeName\":\"SortcodeAccountNumber\",\"identification\":\"04066801234567\"}")
            StringAssert.Contains(call.Body, "\"creditorAccount\":{\"schemeName\":\"BBan\",\"identification\":\"CLRB04066887654321\"}")
            Assert.IsFalse(call.Body.Contains "null")
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``a CHAPS return sends the ISO reason code`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.Created, """{"paymentId":"55555555-5555-5555-5555-555555555555"}""")
            let paymentReturn = ChapsV6.CreateReturnPaymentDtoV6(Guid "55555555-5555-5555-5555-555555555555", ChapsV6.ReturnReasonCodeV6.Ac04, "instr-3", None)
            let! result = Api.chapsReturnPaymentAsync (clientWith handler) (Guid.NewGuid()) paymentReturn ct
            expectOk result |> ignore
            equal("""{"paymentId":"55555555-5555-5555-5555-555555555555","reason":"AC04","instructionIdentification":"instr-3"}""", (handler.Calls |> List.exactlyOne).Body)
        }

    [<TestMethod>]
    member _.``the secondary reference check reads Required and BankName`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"Data":{"Required":true,"BankName":"Building Society","Identification":"04000412345678"}}""")
            let! result = Api.requiresSecondaryReferenceAsync (clientWith handler) (Guid.NewGuid()) "040004" "12345678" ct
            let check = expectOk result
            Assert.IsTrue check.Required
            equal(ValueSome "Building Society", check.BankName)
            equal("/open-banking/outbound/v1/srd/validate", (handler.Calls |> List.exactlyOne).PathAndQuery)
        }

    [<TestMethod>]
    member _.``opting a virtual account out of CoP sends the reason`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let accountId, virtualId = Guid.NewGuid(), Guid.NewGuid()
            let! result = Api.setCopParticipationAsync (clientWith handler) (Guid.NewGuid()) (AccountRef.Virtual(accountId, virtualId)) (CopParticipation.OptOut "Pooled account") ct
            expectOk result
            let call = handler.Calls |> List.exactlyOne
            equal("PUT", call.Method)
            equal($"/v1/Cop/opt/accounts/{accountId:D}/virtual/{virtualId:D}", call.PathAndQuery)
            equal("""{"optOut":true,"optOutReason":"Pooled account"}""", call.Body)
        }

    [<TestMethod>]
    member _.``multi-currency updates send Authorization, signature and request id in their own headers`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, "{}")
            let requestId = Guid.NewGuid()
            let request = UK.MultiCurrency.MccyTransactionsV1.UpdateAccountRequest()
            let! result = UK.MultiCurrency.Api.updateAccountAsync (clientWith handler) requestId (Guid.NewGuid()) request ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            equal("Bearer api-token", call.Headers["Authorization"])
            equal(requestId.ToString "N", call.Headers["X-Request-ID"])
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``closing a multi-currency account sends Authorization and the reason`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.NoContent, "")
            let accountId = Guid.NewGuid()
            let! result = UK.MultiCurrency.Api.closeAccountAsync (clientWith handler) (Guid.NewGuid()) accountId (ValueSome "AccountSwitched") ct
            expectOk result
            let call = handler.Calls |> List.exactlyOne
            equal("DELETE", call.Method)
            equal($"/mccy/v1/Accounts/{accountId:D}?accountCloseReason=AccountSwitched", call.PathAndQuery)
            equal("Bearer api-token", call.Headers["Authorization"])
            Assert.IsFalse(call.Headers.ContainsKey "DigitalSignature")
        }

    [<TestMethod>]
    member _.``Faster Payments post the signed batch with two-decimal amounts`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.Accepted, """{"transactions":[],"halLinks":[]}""")

            let transfer =
                createCreditTransfer
                    { To = UK_Domestic("20-20-15", "55555555")
                      AccountHolder = "Mr Test"
                      Sum = 12.5m
                      Currency = "GBP"
                      Description = "Phone Bill"
                      PaymentReference = "123456789"
                      TransactionId = "e2e-12345" }

            let instruction = createPaymentInstruction "Address" None "Batch123" (UK_Domestic("04-06-05", "00000001")) [| transfer |]
            let! result = Api.transferPaymentsAsync (clientWith handler) (Guid.NewGuid()) [| instruction |] ct
            expectOk result |> ignore
            let call = handler.Calls |> List.exactlyOne
            StringAssert.Contains(call.Body, "12.50")
            StringAssert.Contains(call.Body, "GBR20201555555555")
            Assert.IsTrue(verifies call)
        }

    [<TestMethod>]
    member _.``multi-currency payments send amounts to the cent`` () : Task =
        task {
            use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.OK, """{"transactions":[]}""")
            let address = UK.MultiCurrency.MccyPaymentsV1.Creditor_Address("Street 1", "Street 2", "77000", "FI", None)

            let creditor =
                UK.MultiCurrency.MccyPaymentsV1.Creditor(
                    "John", address, Some "NDEAFIHH", null, None, null, Some "FI2112345600000785", None,
                    UK.MultiCurrency.MccyPaymentsV1.Creditor_SchemeName(None, Some "IBAN"))

            let item (amount: float) =
                UK.MultiCurrency.MccyPaymentsV1.PaymentRequestItem(
                    "e2e", "ref", amount, creditor, "Jim Doe",
                    UK.MultiCurrency.MccyPaymentsV1.PaymentRequestItem_DebtorAddress("Street 1", "Street 2", "77000", "GB", None),
                    UK.MultiCurrency.MccyPaymentsV1.AccountIdentifier(UK.MultiCurrency.MccyPaymentsV1.AccountIdentifierKind.Iban, "GB33CLRB04066812345678"),
                    "EUR", None, null, null, null, None, null, null, null)

            let! result = UK.MultiCurrency.Api.transferPaymentsAsync (clientWith handler) (Guid.NewGuid()) None "EUR" [| item 1234567.89; item 123. |] ct
            expectOk result |> ignore
            let body = (handler.Calls |> List.exactlyOne).Body
            StringAssert.Contains(body, "\"amount\":1234567.89,")
            StringAssert.Contains(body, "\"amount\":123.00,")
        }

    [<TestMethod>]
    member _.``payment amounts use a decimal point whatever the current culture`` () : Task =
        task {
            let original = Globalization.CultureInfo.CurrentCulture
            Globalization.CultureInfo.CurrentCulture <- Globalization.CultureInfo "fi-FI"

            try
                use handler = new FakeClearBank(fun _ _ -> HttpStatusCode.Created, """{"paymentId":"55555555-5555-5555-5555-555555555555","transactions":[],"halLinks":[]}""")
                let client = clientWith handler

                let transfer =
                    createCreditTransfer
                        { To = UK_Domestic("20-20-15", "55555555"); AccountHolder = "Mr Test"; Sum = 1234567.89m; Currency = "GBP"
                          Description = "Phone Bill"; PaymentReference = "123456789"; TransactionId = "e2e-12345" }

                let instruction = createPaymentInstruction "Address" None "Batch123" (UK_Domestic("04-06-05", "00000001")) [| transfer |]
                let! fps = Api.transferPaymentsAsync client (Guid.NewGuid()) [| instruction |] ct

                let address = ChapsV6.PartyAddressDetailsDtoV6("London", "GB", "EC3A 8EP", None, None, None)
                let party name = ChapsV6.CustomerPartyDetailsDtoV6(name, address, ChapsV6.OrganisationIdentifierV6(Some "CLRBGB22", None, None), null)

                let payment =
                    ChapsV6.CreateCustomerPaymentDtoV6(
                        "instr-1", "e2e-1", ChapsV6.InterBankSettlementAmountDtoV6(1234567.89, "GBP"),
                        chapsAccount (IBAN "GB33CLRB04066812345678"), chapsAccount (IBAN "GB33CLRB04066812345678"), party "A",
                        chapsAccount (IBAN "GB33CLRB04066887654321"), party "B",
                        ChapsV6.PurposeCodeV6.Loan, ChapsV6.CategoryPurposeCodeV6.Loan, ChapsV6.StructuredRemittanceInformationDtoV6(None))

                let! chaps = Api.chapsCustomerPaymentAsync client (Guid.NewGuid()) payment ct
                expectOk fps |> ignore
                expectOk chaps |> ignore

                for call in handler.Calls do
                    StringAssert.Contains(call.Body, "1234567.89")
            finally
                Globalization.CultureInfo.CurrentCulture <- original
        }

[<TestClass>]
type ``Webhook Type Tests`` () =

    [<TestMethod>]
    member _.``UK webhook types parse, including misspelt aliases and unlisted types`` () =
        equal(Webhooks.UkWebhook.TransactionSettled, Webhooks.UkWebhook.parse Webhooks.WebhookTypes.UK.TransactionSettled)
        equal(Webhooks.UkWebhook.PaymentMessageAssessmentFailed, Webhooks.UkWebhook.parse Webhooks.WebhookTypes.UK.PaymentMessageAssesmentFailed)
        equal(Webhooks.UkWebhook.PaymentMessageAssessmentFailed, Webhooks.UkWebhook.parse Webhooks.WebhookTypes.UK.PaymentMessageAssessmentFailed)
        equal(Webhooks.UkWebhook.OutboundHeldTransaction, Webhooks.UkWebhook.parse "OuboundHeldTransaction")
        equal(Webhooks.UkWebhook.BacsMandateInitiated, Webhooks.UkWebhook.parse "BacsMandateInitiatedV2")
        equal(Webhooks.UkWebhook.Unlisted "ChequePaid", Webhooks.UkWebhook.parse "ChequePaid")
        equal("BacsUnpaidDirectDebitReceivedV3", Webhooks.UkWebhook.name Webhooks.UkWebhook.BacsUnpaidDirectDebitReceived)
        equal("ChequePaid", Webhooks.UkWebhook.name (Webhooks.UkWebhook.Unlisted "ChequePaid"))

    [<TestMethod>]
    member _.``a parsed UK payload gives its typed webhook type`` () =
        let payload = Webhooks.parsePaymentsCallUK """{"Type":"TransactionSettled","Version":6,"Payload":{},"Nonce":1}"""
        equal(Webhooks.UkWebhook.TransactionSettled, Webhooks.webhookTypeUK payload)

    [<TestMethod>]
    member _.``EU webhook types round-trip through their names`` () =
        let name = Webhooks.WebhookTypes.EU.``Sepa.Instant.RecallPayment.Settled``
        equal(Webhooks.EuWebhook.SepaInstantRecallPaymentSettled, Webhooks.EuWebhook.parse name)
        equal(name, Webhooks.EuWebhook.name Webhooks.EuWebhook.SepaInstantRecallPaymentSettled)
        equal(Webhooks.EuWebhook.Unlisted "Something.New", Webhooks.EuWebhook.parse "Something.New")

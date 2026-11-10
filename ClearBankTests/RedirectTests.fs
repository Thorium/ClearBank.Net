namespace ClearBankTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open ClearBank.Common

/// A local host that answers one request, once it has read its head and body.
type private Host(answer: string) =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    let mutable called = false
    let mutable received = ""
    do listener.Start()

    let rec readRequest (stream: NetworkStream) (buffer: byte array) (read: string) =
        task {
            let! count = stream.ReadAsync(buffer, 0, buffer.Length)
            let text = read + Encoding.ASCII.GetString(buffer, 0, count)
            let headEnd = text.IndexOf "\r\n\r\n"
            let declared = RegularExpressions.Regex.Match(text, "(?i)content-length: (\\d+)")
            let bodyLength = if declared.Success then int declared.Groups[1].Value else 0

            if count = 0 || (headEnd >= 0 && text.Length - headEnd - 4 >= bodyLength) then
                return text
            else
                return! readRequest stream buffer text
        }

    let served =
        task {
            try
                use! socket = listener.AcceptTcpClientAsync()
                called <- true
                use stream = socket.GetStream()
                let! request = readRequest stream (Array.zeroCreate 16384) ""
                received <- request
                let bytes = Encoding.ASCII.GetBytes answer
                do! stream.WriteAsync(bytes, 0, bytes.Length)
            with
            | :? SocketException
            | :? IO.IOException
            | :? ObjectDisposedException -> ()
        }

    member _.Port = (listener.LocalEndpoint :?> IPEndPoint).Port
    member _.Called = called
    /// The request as it arrived: head and body.
    member _.Received = received
    member _.Served: Task = served

    interface IDisposable with
        member _.Dispose() = listener.Stop()

/// A payment's body and signature stay on ClearBank's host.
[<TestClass>]
type ``Redirect Tests`` () =

    let okAnswer = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"

    let acceptedAnswer =
        "HTTP/1.1 202 Accepted\r\nContent-Type: application/json\r\nContent-Length: 33\r\nConnection: close\r\n\r\n{\"transactions\":[],\"halLinks\":[]}"

    let redirectWith (status: int) (port: int) =
        $"HTTP/1.1 {status} Moved\r\nLocation: http://127.0.0.1:{port}/stolen\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"

    let redirectTo (port: int) = redirectWith 307 port

    let payment =
        let transfer =
            ClearBank.UK.createCreditTransfer
                { To = UK_Domestic("20-20-15", "55555555"); AccountHolder = "Mr Test"; Sum = 12.5m; Currency = "GBP"
                  Description = "Phone Bill"; PaymentReference = "123456789"; TransactionId = "e2e-12345" }

        ClearBank.UK.createPaymentInstruction "Address" None "Batch123" (UK_Domestic("04-06-05", "00000001")) [| transfer |]

    /// The call failed because its answer came from the URI a redirect led to.
    let expectRedirectError (sent: string) (answered: string) (result: Result<'T, exn * string>) =
        match result with
        | Ok _ -> Assert.Fail "expected an error: the answer came from another host"
        | Error(err, _) ->
            match err.GetBaseException() with
            | :? ClearBankRedirectException as redirect ->
                equal(Uri sent, redirect.Sent)
                equal(Uri answered, redirect.Answered)
                StringAssert.Contains(redirect.Message, "redirect")
            | other -> Assert.Fail $"expected ClearBankRedirectException, got {other}"

    let throughClientMadeHere (log: (HttpStatusCode * string -> unit) option) : Task =
        task {
            use other = new Host(okAnswer)
            use first = new Host(redirectTo other.Port)
            use http = ClearBankClient.createHttpClient $"http://127.0.0.1:{first.Port}/" log
            use! response = http.PostAsync("v3/payments", new StringContent "{}")
            Assert.AreEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode)
            do! first.Served
            Assert.IsFalse other.Called
        }

    let throughConfiguration (log: (HttpStatusCode * string -> unit) option) : Task =
        task {
            use other = new Host(okAnswer)
            use first = new Host(redirectTo other.Port)
            let config =
                { BaseUrl = $"http://127.0.0.1:{first.Port}/"
                  PrivateKey = "the-token"
                  AzureKeyVaultName = "vault"
                  AzureKeyVaultCredentials = DefaultCredentials
                  LogUnsuccessfulHandler = log }
            let! result = ClearBank.UK.getAccounts config |> Async.StartAsTask
            do! first.Served
            Assert.IsTrue(Result.isError result)
            Assert.IsFalse other.Called
        }

    [<TestMethod>]
    member _.``the client made here stops at a redirect`` () = throughClientMadeHere None

    [<TestMethod>]
    member _.``the client made here stops at a redirect, when logging`` () = throughClientMadeHere (Some ignore)

    [<TestMethod>]
    member _.``a configuration-based call stops at a redirect`` () = throughConfiguration None

    [<TestMethod>]
    member _.``a configuration-based call stops at a redirect, when logging`` () = throughConfiguration (Some ignore)

    [<TestMethod>]
    member _.``a payment whose client followed a redirect is an Error, not a success`` () : Task =
        task {
            for status in [ 307; 308; 302; 301 ] do
                use other = new Host(acceptedAnswer)
                use first = new Host(redirectWith status other.Port)
                use http = new HttpClient(BaseAddress = Uri $"http://127.0.0.1:{first.Port}/")
                let! result = ClearBank.UK.Api.transferPaymentsAsync (ClearBankClient.create http "the-token" (signer ())) (Guid.NewGuid()) [| payment |] ct
                do! first.Served
                do! other.Served
                Assert.IsTrue(other.Called, "the caller's handler follows redirects")
                expectRedirectError $"http://127.0.0.1:{first.Port}/v3/Payments/FPS" $"http://127.0.0.1:{other.Port}/stolen" result
        }

    [<TestMethod>]
    member _.``a call sent without a generated client reports a followed redirect too`` () : Task =
        task {
            use other = new Host(okAnswer)
            use first = new Host(redirectWith 307 other.Port)
            use http = new HttpClient(BaseAddress = Uri $"http://127.0.0.1:{first.Port}/")
            let accountId = Guid.NewGuid()

            let! result =
                ClearBank.UK.Api.setCopParticipationAsync
                    (ClearBankClient.create http "the-token" (signer ()))
                    (Guid.NewGuid())
                    (ClearBank.UK.AccountRef.Real accountId)
                    ClearBank.UK.CopParticipation.Participate
                    ct

            do! first.Served
            do! other.Served
            expectRedirectError $"http://127.0.0.1:{first.Port}/v1/Cop/opt/accounts/{accountId:D}" $"http://127.0.0.1:{other.Port}/stolen" result
        }

    [<TestMethod>]
    member _.``a read whose client followed a redirect is an Error`` () : Task =
        task {
            use other = new Host(okAnswer)
            use first = new Host(redirectWith 302 other.Port)
            use http = new HttpClient(BaseAddress = Uri $"http://127.0.0.1:{first.Port}/")
            let! result = ClearBank.UK.Api.getEveryAccountAsync (ClearBankClient.create http "the-token" (signer ())) ValueNone ct
            do! first.Served
            do! other.Served
            expectRedirectError $"http://127.0.0.1:{first.Port}/v3/Accounts?pageNumber=1" $"http://127.0.0.1:{other.Port}/stolen" result
        }

    [<TestMethod>]
    member _.``an answer from the host asked is not taken for a redirect`` () : Task =
        task {
            use host = new Host(acceptedAnswer)
            use http = new HttpClient(BaseAddress = Uri $"http://127.0.0.1:{host.Port}/")
            let! result = ClearBank.UK.Api.transferPaymentsAsync (ClearBankClient.create http "the-token" (signer ())) (Guid.NewGuid()) [| payment |] ct
            do! host.Served
            expectOk result |> ignore
            StringAssert.Contains(host.Received, "POST /v3/Payments/FPS")
            StringAssert.Contains(host.Received, "GBR20201555555555")
        }

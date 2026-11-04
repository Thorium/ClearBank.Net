namespace ClearBankTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open ClearBank.Common

/// A local host that answers one request.
type private Host(answer: string) =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    let mutable called = false
    do listener.Start()

    let served =
        task {
            try
                use! socket = listener.AcceptTcpClientAsync()
                called <- true
                use stream = socket.GetStream()
                let buffer = Array.zeroCreate 16384
                let! _ = stream.ReadAsync(buffer, 0, buffer.Length)
                let bytes = Encoding.ASCII.GetBytes answer
                do! stream.WriteAsync(bytes, 0, bytes.Length)
            with
            | :? SocketException
            | :? ObjectDisposedException -> ()
        }

    member _.Port = (listener.LocalEndpoint :?> IPEndPoint).Port
    member _.Called = called
    member _.Served: Task = served

    interface IDisposable with
        member _.Dispose() = listener.Stop()

/// A payment's body and signature stay on ClearBank's host.
[<TestClass>]
type ``Redirect Tests`` () =

    let okAnswer = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}"

    let redirectTo (port: int) =
        $"HTTP/1.1 307 Moved\r\nLocation: http://127.0.0.1:{port}/stolen\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"

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

namespace ClearBankTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Reflection
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open ClearBank
open ClearBank.Common
open ClearBank.UK

/// Holds every request until the caller's token fires, or answers after a few seconds.
type StuckClearBank() =
    inherit HttpMessageHandler()
    let entered = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
    let sawCancel = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

    member _.Entered: Task = entered.Task
    member _.SawCancel: Task = sawCancel.Task

    override _.SendAsync(_, token) =
        task {
            entered.TrySetResult() |> ignore

            try
                do! Task.Delay(TimeSpan.FromSeconds 3., token)
            with :? OperationCanceledException ->
                sawCancel.TrySetResult() |> ignore
                raise (OperationCanceledException token)

            return new HttpResponseMessage(HttpStatusCode.OK, Content = new StringContent "{}")
        }

/// The caller's token reaches the HTTP call of every operation.
[<TestClass>]
type ``Cancellation Tests`` () =

    let id1, id2, id3 = Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()
    let real, virt = AccountRef.Real id1, AccountRef.Virtual(id1, id2)

    let payee =
        { SortCode = "04-06-05"
          AccountNumber = "00000001"
          OwnerName = ",,Owner"
          LegalOwnerType = LegalOwnerType.Personal
          SecondaryReference = ValueNone
          EndToEndId = ValueNone }

    let entry name (call: ClearBankClient -> CancellationToken -> 'T :> Task) =
        name, (fun client token -> call client token :> Task)

    let everyCall =
        [ entry "testAsync" (fun c t -> Api.testAsync c id3 t)
          entry "getTransactionsAsync" (fun c t -> Api.getTransactionsAsync c ValueNone ValueNone ValueNone ValueNone t)
          entry "getEveryTransactionAsync" (fun c t -> Api.getEveryTransactionAsync c ValueNone ValueNone ValueNone t)
          entry "getAccountTransactionsAsync real" (fun c t -> Api.getAccountTransactionsAsync c real ValueNone ValueNone ValueNone ValueNone t)
          entry "getAccountTransactionsAsync virtual" (fun c t -> Api.getAccountTransactionsAsync c virt ValueNone ValueNone ValueNone ValueNone t)
          entry "getEveryAccountTransactionAsync" (fun c t -> Api.getEveryAccountTransactionAsync c real ValueNone ValueNone ValueNone t)
          entry "getAccountTransactionAsync real" (fun c t -> Api.getAccountTransactionAsync c real id3 t)
          entry "getAccountTransactionAsync virtual" (fun c t -> Api.getAccountTransactionAsync c virt id3 t)
          entry "returnTransactionsAsync real" (fun c t -> Api.returnTransactionsAsync c id3 real [ id2, "0" ] t)
          entry "returnTransactionsAsync virtual" (fun c t -> Api.returnTransactionsAsync c id3 virt [ id2, "0" ] t)
          entry "getUnpaidDirectDebitsAsync" (fun c t -> Api.getUnpaidDirectDebitsAsync c "04-06-05" BacsItemQuery.empty ValueNone ValueNone t)
          entry "getUnappliedDirectCreditsAsync" (fun c t -> Api.getUnappliedDirectCreditsAsync c "04-06-05" BacsItemQuery.empty ValueNone ValueNone t)
          entry "createVirtualAccountsAsync" (fun c t -> Api.createVirtualAccountsAsync c id3 id1 [ { OwnerName = "Owner"; ExternalIdentifier = ValueNone } ] t)
          entry "getVirtualAccountsAsync" (fun c t -> Api.getVirtualAccountsAsync c id1 ValueNone ValueNone t)
          entry "getAllVirtualAccountsAsync" (fun c t -> Api.getAllVirtualAccountsAsync c ValueNone ValueNone t)
          entry "getVirtualAccountAsync" (fun c t -> Api.getVirtualAccountAsync c id1 id2 t)
          entry "disableVirtualAccountAsync" (fun c t -> Api.disableVirtualAccountAsync c id3 id1 id2 t)
          entry "getMandatesAsync real" (fun c t -> Api.getMandatesAsync c real ValueNone ValueNone t)
          entry "getMandatesAsync virtual" (fun c t -> Api.getMandatesAsync c virt ValueNone ValueNone t)
          entry "cancelMandateAsync real" (fun c t -> Api.cancelMandateAsync c id3 real id2 "0" t)
          entry "cancelMandateAsync virtual" (fun c t -> Api.cancelMandateAsync c id3 virt id2 "0" t)
          entry "returnMandateAsync real" (fun c t -> Api.returnMandateAsync c id3 real id2 "reason" t)
          entry "returnMandateAsync virtual" (fun c t -> Api.returnMandateAsync c id3 virt id2 "reason" t)
          entry "getMandateCollectionsAsync real" (fun c t -> Api.getMandateCollectionsAsync c real id2 ValueNone ValueNone ValueNone ValueNone t)
          entry "getMandateCollectionsAsync virtual" (fun c t -> Api.getMandateCollectionsAsync c virt id2 ValueNone ValueNone ValueNone ValueNone t)
          entry "getUnpaidCollectionsAsync real" (fun c t -> Api.getUnpaidCollectionsAsync c real ValueNone ValueNone ValueNone ValueNone ValueNone ValueNone t)
          entry "getUnpaidCollectionsAsync virtual" (fun c t -> Api.getUnpaidCollectionsAsync c virt ValueNone ValueNone ValueNone ValueNone ValueNone ValueNone t)
          entry "createAccountAsync" (fun c t -> Api.createAccountAsync c id3 "04-06-05" "Account" "Owner" t)
          entry "getAccountsAsync" (fun c t -> Api.getAccountsAsync c ValueNone ValueNone t)
          entry "getEveryAccountAsync" (fun c t -> Api.getEveryAccountAsync c ValueNone t)
          entry "getAccountAsync" (fun c t -> Api.getAccountAsync c id1 t)
          entry "updateAccountAsync" (fun c t -> Api.updateAccountAsync c id3 id1 AccountUpdate.empty t)
          entry "closeAccountAsync" (fun c t -> Api.closeAccountAsync c id3 id1 AccountStatusReason.AccountSwitched t)
          entry "requestAccountClosureAsync" (fun c t -> Api.requestAccountClosureAsync c id3 id1 AccountStatusReason.AccountSwitched t)
          entry "getAccountClosureAsync" (fun c t -> Api.getAccountClosureAsync c id1 t)
          entry "updateVirtualAccountAsync" (fun c t -> Api.updateVirtualAccountAsync c id3 id1 id2 VirtualAccountUpdate.empty t)
          entry "transferPaymentsAsync" (fun c t -> Api.transferPaymentsAsync c id3 Unchecked.defaultof<_> t)
          entry "verifyPayeeAsync" (fun c t -> Api.verifyPayeeAsync c id3 payee t)
          entry "requiresSecondaryReferenceAsync" (fun c t -> Api.requiresSecondaryReferenceAsync c id3 "04-06-05" "00000001" t)
          entry "setCopParticipationAsync" (fun c t -> Api.setCopParticipationAsync c id3 real CopParticipation.Participate t)
          entry "chapsCustomerPaymentAsync" (fun c t -> Api.chapsCustomerPaymentAsync c id3 Unchecked.defaultof<_> t)
          entry "chapsInstitutionPaymentAsync" (fun c t -> Api.chapsInstitutionPaymentAsync c id3 Unchecked.defaultof<_> t)
          entry "chapsReturnPaymentAsync" (fun c t -> Api.chapsReturnPaymentAsync c id3 Unchecked.defaultof<_> t)
          entry "MultiCurrency.createAccountAsync" (fun c t ->
              MultiCurrency.Api.createAccountAsync c id3 "04-06-05" "Account" "Owner" MultiCurrency.AccountKind.GeneralClient Unchecked.defaultof<_> Unchecked.defaultof<_> Unchecked.defaultof<_> Unchecked.defaultof<_> t)
          entry "MultiCurrency.getAccountsAsync" (fun c t -> MultiCurrency.Api.getAccountsAsync c ValueNone ValueNone t)
          entry "MultiCurrency.getTransactionsAsync" (fun c t -> MultiCurrency.Api.getTransactionsAsync c id1 "EUR" ValueNone ValueNone ValueNone ValueNone t)
          entry "MultiCurrency.getAccountAsync" (fun c t -> MultiCurrency.Api.getAccountAsync c id1 t)
          entry "MultiCurrency.getAccountBalancesAsync" (fun c t -> MultiCurrency.Api.getAccountBalancesAsync c id1 t)
          entry "MultiCurrency.getTransactionAsync" (fun c t -> MultiCurrency.Api.getTransactionAsync c id2 t)
          entry "MultiCurrency.updateAccountAsync" (fun c t -> MultiCurrency.Api.updateAccountAsync c id3 id1 Unchecked.defaultof<_> t)
          entry "MultiCurrency.updateAccountStatusAsync" (fun c t -> MultiCurrency.Api.updateAccountStatusAsync c id3 id1 Unchecked.defaultof<_> t)
          entry "MultiCurrency.addCurrencyAsync" (fun c t -> MultiCurrency.Api.addCurrencyAsync c id3 id1 Unchecked.defaultof<_> t)
          entry "MultiCurrency.closeAccountAsync" (fun c t -> MultiCurrency.Api.closeAccountAsync c id3 id1 ValueNone t)
          entry "MultiCurrency.transferPaymentsAsync" (fun c t -> MultiCurrency.Api.transferPaymentsAsync c id3 None "EUR" Unchecked.defaultof<_> t)
          entry "MultiCurrency.cancelPaymentsAsync batch" (fun c t -> MultiCurrency.Api.cancelPaymentsAsync c id3 "batch" ValueNone t)
          entry "MultiCurrency.cancelPaymentsAsync payment" (fun c t -> MultiCurrency.Api.cancelPaymentsAsync c id3 "batch" (ValueSome "payment") t)
          entry "EU.sepaTransferPaymentsAsync" (fun c t -> EU.Api.sepaTransferPaymentsAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.sepaInstantTransferPaymentsAsync" (fun c t -> EU.Api.sepaInstantTransferPaymentsAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.sepaRecallResponseAsync" (fun c t -> EU.Api.sepaRecallResponseAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.sepaRecallRequestAsync" (fun c t -> EU.Api.sepaRecallRequestAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.sepaPaymentReturnAsync" (fun c t -> EU.Api.sepaPaymentReturnAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.sepaInstantRecallResponseAsync" (fun c t -> EU.Api.sepaInstantRecallResponseAsync c id3 id2 Unchecked.defaultof<_> t)
          entry "EU.sepaInstantRecallRequestAsync" (fun c t -> EU.Api.sepaInstantRecallRequestAsync c id3 id2 Unchecked.defaultof<_> t)
          entry "EU.target2InstitutionPaymentAsync" (fun c t -> EU.Api.target2InstitutionPaymentAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.target2CustomerPaymentAsync" (fun c t -> EU.Api.target2CustomerPaymentAsync c id3 Unchecked.defaultof<_> t)
          entry "EU.target2PaymentReturnAsync" (fun c t -> EU.Api.target2PaymentReturnAsync c id3 Unchecked.defaultof<_> t) ]

    /// The public operations of an Api module: functions taking the token last.
    let operations (prefix: string) (api: Type) =
        api.GetMethods(BindingFlags.Public ||| BindingFlags.Static)
        |> Array.filter (fun m -> m.GetParameters() |> Array.tryLast |> Option.exists (fun p -> p.ParameterType = typeof<CancellationToken>))
        |> Array.map (fun m -> prefix + m.Name)
        |> Array.toList

    /// The name of a call whose token did not reach the handler, or whose task did not end cancelled.
    let failure (name: string, call: ClearBankClient -> CancellationToken -> Task) =
        task {
            let handler = new StuckClearBank()
            use http = new HttpClient(handler, BaseAddress = Uri "https://institution-api-sim.clearbank.co.uk/")
            use cts = new CancellationTokenSource()
            let running = call (ClearBankClient.create http "api-token" (signer ())) cts.Token
            do! Task.WhenAny(handler.Entered, running) :> Task
            cts.Cancel()
            do! Task.WhenAny(handler.SawCancel, Task.Delay(TimeSpan.FromSeconds 2.)) :> Task
            do! Task.WhenAny(running, Task.Delay(TimeSpan.FromSeconds 10.)) :> Task

            return
                match handler.SawCancel.IsCompleted, running.IsCanceled with
                | true, true -> ValueNone
                | sawCancel, cancelled -> ValueSome $"{name} (sawCancel={sawCancel}, cancelled={cancelled})"
        }

    /// A port that accepts connections and never answers, and a configuration pointing at it.
    let silentPort log =
        let silent = new TcpListener(IPAddress.Loopback, 0)
        silent.Start()

        silent,
        { BaseUrl = $"http://127.0.0.1:{(silent.LocalEndpoint :?> IPEndPoint).Port}/"
          PrivateKey = "api-token"
          AzureKeyVaultName = "vault"
          AzureKeyVaultCredentials = DefaultCredentials
          LogUnsuccessfulHandler = log }

    let stopped (call: Async<'T>) =
        task {
            use cts = new CancellationTokenSource()
            let running = Async.StartAsTask(call, cancellationToken = cts.Token)
            do! Task.Delay(TimeSpan.FromMilliseconds 100.)
            cts.Cancel()
            do! Task.WhenAny(running :> Task, Task.Delay(TimeSpan.FromSeconds 3.)) :> Task
            return running
        }

    let endsCanceled (log: (HttpStatusCode * string -> unit) option) : Task =
        task {
            let silent, config = silentPort log
            use _ = silent

            let check (name: string) (running: Task<'T>) =
                equal((name, TaskStatus.Canceled), (name, running.Status))

            let! accounts = stopped (getAccounts config)
            check "getAccounts" accounts
            let! transactions = stopped (getTransactions config None None None None)
            check "getTransactions" transactions
            let! accountTransaction = stopped (getAccountTransaction config (string (Guid.NewGuid())) (string (Guid.NewGuid())))
            check "getAccountTransaction" accountTransaction
            let! multiCurrencyAccount = stopped (MultiCurrency.getAccount config (Guid.NewGuid()))
            check "MultiCurrency.getAccount" multiCurrencyAccount
        }

    [<TestMethod>]
    member _.``cancelling stops every operation's HTTP call`` () : Task =
        task {
            // A new RSA key is generated on first use, and generating it concurrently can dispose it under another signer.
            let! _ = (signer ()).Sign "" ct
            let! outcomes = everyCall |> List.map failure |> Task.WhenAll
            let failures = outcomes |> Array.choose ValueOption.toOption
            equal([||], failures)
        }

    [<TestMethod>]
    member _.``the list above holds every public operation`` () =
        let assembly = typeof<ClearBankClient>.Assembly
        let uk = assembly.GetType "ClearBank.UK"

        let expected =
            operations "" (uk.GetNestedType "Api")
            @ operations "MultiCurrency." ((uk.GetNestedType "MultiCurrency").GetNestedType "Api")
            @ operations "EU." ((assembly.GetType "ClearBank.EU").GetNestedType "Api")

        Assert.IsTrue(expected.Length > 50, $"Found {expected.Length} operations")
        let listed = everyCall |> List.map fst
        let missing = expected |> List.filter (fun name -> not (listed |> List.exists (fun call -> call = name || call.StartsWith(name + " "))))
        equal(([]: string list), missing)

    [<TestMethod>]
    member _.``cancelling a configuration-based call ends it Canceled`` () = endsCanceled None

    [<TestMethod>]
    member _.``cancelling a configuration-based call ends it Canceled, when logging`` () = endsCanceled (Some ignore)

    [<TestMethod>]
    member _.``awaiting a cancelled configuration-based call throws OperationCanceledException`` () : Task =
        task {
            let silent, config = silentPort None
            use _ = silent
            let! running = stopped (getAccounts config)
            let! thrown = Assert.ThrowsExceptionAsync<TaskCanceledException>(fun () -> running :> Task)
            Assert.IsInstanceOfType<OperationCanceledException> thrown
        }

    [<TestMethod>]
    member _.``running a cancelled configuration-based call synchronously throws OperationCanceledException`` () =
        let silent, config = silentPort None
        use _ = silent
        use cts = new CancellationTokenSource(TimeSpan.FromMilliseconds 100.)
        Assert.ThrowsException<OperationCanceledException>(fun () ->
            Async.RunSynchronously(getAccounts config, cancellationToken = cts.Token) |> ignore)
        |> ignore

    [<TestMethod>]
    member _.``a timeout on a configuration-based call stays an Error`` () : Task =
        task {
            let silent, config = silentPort None
            use _ = silent

            let call =
                perCall config RequestSigner.unavailable None (fun client token ->
                    client.Http.Timeout <- TimeSpan.FromMilliseconds 200.
                    Api.getAccountsAsync client ValueNone ValueNone token)

            use cts = new CancellationTokenSource()

            match! Async.StartAsTask(call, cancellationToken = cts.Token) with
            | Ok _ -> Assert.Fail "expected an error"
            | Error(err, _) -> Assert.IsFalse cts.IsCancellationRequested; Assert.IsNotNull err
        }

    [<TestMethod>]
    member _.``a task cancelled by another token stays a failure`` () : Task =
        task {
            let silent, config = silentPort None
            use _ = silent
            use other = new CancellationTokenSource()
            do! other.CancelAsync()

            let call =
                perCall config RequestSigner.unavailable None (fun _ _ ->
                    Task.FromCanceled<Result<unit, exn * string>> other.Token)

            let running = Async.StartAsTask call
            do! Task.WhenAny(running :> Task, Task.Delay(TimeSpan.FromSeconds 3.)) :> Task
            equal(TaskStatus.Faulted, running.Status)
        }

    [<TestMethod>]
    member _.``a configuration-based failure is still an Error while the token is live`` () : Task =
        task {
            use refused = new TcpListener(IPAddress.Loopback, 0)
            refused.Start()
            let port = (refused.LocalEndpoint :?> IPEndPoint).Port
            refused.Stop()

            let config =
                { BaseUrl = $"http://127.0.0.1:{port}/"
                  PrivateKey = "api-token"
                  AzureKeyVaultName = "vault"
                  AzureKeyVaultCredentials = DefaultCredentials
                  LogUnsuccessfulHandler = None }

            use cts = new CancellationTokenSource()
            let! result = Async.StartAsTask(getAccounts config, cancellationToken = cts.Token)
            Assert.IsTrue(Result.isError result)
        }

module Program =

    [<EntryPoint>]
    let main _ =
        task {
            let t = ClearBankTests.``UK Tests``()
            do! t.TestMethodPassingTest()
            do! t.ProcessPaymentsTest()
            //do! t.CreateAccountTest()
            do! t.GetAccountsTest()
            do! t.WebhookResponseTest()
            return 0
        } |> Async.AwaitTask |> Async.RunSynchronously

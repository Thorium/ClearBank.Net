module Program =

    [<EntryPoint>]
    let main _ =
        let t = ClearBankTests.TestClass()
        t.TestMethodPassingTest()
        t.ProcessPaymentsTest()
        //t.CreateAccountTest()
        t.GetAccountsTest()
        t.WebhookResponseTest()
        0

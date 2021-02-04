module Program =

    [<EntryPoint>]
    let main _ =
        let t = ClearBankTests.TestClass()
        t.TestMethodPassingTest()
        t.ProcessPaymentsTest()
        //t.CreateAccountTest()
        0

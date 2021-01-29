module Program =

    [<EntryPoint>]
    let main _ =
        let t = ClearBankTests.TestClass()
        t.TestMethodPassingTest()
        t.ProcessPaymentsTest()
        0

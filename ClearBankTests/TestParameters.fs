/// To run the unit-tests against a ClearBank account, e.g. https://institution-api-sim.clearbank.co.uk/
/// you have to configure these test-parameters for your environment
module TestParameters

open ClearBank.Common

[<Literal>]
let clearbankPrivateKey = "..."
[<Literal>]
let azureKeyVaultName = "myVault"
[<Literal>]
let azureKeyVaultCertificateName = "myCert"
[<Literal>]
let sortCode = "04-06-05"
let transferFromAccount = UK_Domestic(sortCode, "00000001")
/// test, prod: "https://institution-api.clearbank.co.uk/"
[<Literal>]
let clearbankUri = "https://institution-api-sim.clearbank.co.uk/"

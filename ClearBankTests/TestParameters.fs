/// To run the unit-tests against a ClearBank account, e.g. https://institution-api-sim.clearbank.co.uk/
/// you have to configure these test-parameters for your environment
module TestParameters

open ClearBank

let clearbankPrivateKey = "..."
let azureKeyVaultName = "myVault"
let azureKeyVaultCertificateName = "myCert"
let sortCode = "04-06-05"
let transferFromAccount = UK_Domestic(sortCode, "00000001")

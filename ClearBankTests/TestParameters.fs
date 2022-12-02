/// To run the unit-tests against a ClearBank account, e.g. https://institution-api-sim.clearbank.co.uk/
/// you have to configure these test-parameters for your environment
module TestParameters

open ClearBank

let clearbankPrivateKey = "..."
let azureKeyVaultName = "myVault"
let azureKeyVaultCertificateName = "myCert"
let transferFromAccount = UK_Domestic("04-06-05", "00000001")

module ClearBank.UK

open System
open System.Net.Http
open System.Net
open System.Text.Json
open SwaggerProvider
open ClearBank.Common


// Schemas: https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints
let [<Literal>]schemaV1 = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v1.json"
let [<Literal>]schemaV2 = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v2.json"
let [<Literal>]schemaV3Accounts = __SOURCE_DIRECTORY__ + @"/Schemas/clearbank-api-v3-accounts.json"
let [<Literal>]schemaV3PaymentsFps = __SOURCE_DIRECTORY__ + @"/Schemas/fps-initiate-payment-v3.json"

type ClearBankSwaggerV1 = OpenApiClientProvider<schemaV1, PreferAsync=true>
type ClearBankSwaggerV2 = OpenApiClientProvider<schemaV2, PreferAsync=true>
type ClearBankOpenApiV3Accounts = OpenApiClientProvider<schemaV3Accounts, PreferAsync=true>
type FpsPaymentsV3 = OpenApiClientProvider<schemaV3PaymentsFps, PreferAsync=true>

type ModelsV3Accounts = ClearBankOpenApiV3Accounts.ClearBank.FI.API.Accounts.Versions.V3.Models
type AccountsV3 = ModelsV3Accounts.Binding.Accounts

type V1Models = ClearBankSwaggerV1.ClearBank.FI.API.Versions.V1.Models
type V2Models = ClearBankSwaggerV2.ClearBank.FI.API.Versions.V2.Models

// Schemas from https://github.com/clearbank/clearbank.github.io/tree/main/data/endpoints; chaps-v6 amounts are format double here (float32 otherwise).
let [<Literal>]schemaSterlingV1 = __SOURCE_DIRECTORY__ + @"/Schemas/sterling-v1.json"
let [<Literal>]schemaCopOutboundV1 = __SOURCE_DIRECTORY__ + @"/Schemas/cop-outbound-v1.json"
let [<Literal>]schemaCopAccountsV1 = __SOURCE_DIRECTORY__ + @"/Schemas/cop-accounts-v1.json"
let [<Literal>]schemaChapsV6 = __SOURCE_DIRECTORY__ + @"/Schemas/chaps-v6.json"

type SterlingV1 = OpenApiClientProvider<schemaSterlingV1, PreferAsync=true>
type CopOutboundV1 = OpenApiClientProvider<schemaCopOutboundV1, PreferAsync=true>
type CopAccountsV1 = OpenApiClientProvider<schemaCopAccountsV1, PreferAsync=true>
type ChapsV6 = OpenApiClientProvider<schemaChapsV6, PreferAsync=true>

type SterlingModels = SterlingV1.ClearBank.FI.API.Accounts.Versions.V1.Models

let ``account to string`` acc =
    match acc with
    | IBAN nr ->
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(iban = Some nr)
    | BBAN nr ->
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                FpsPaymentsV3.BatchCounterpartAccountGenericIdentification(
                    nr,
                    schemeName = FpsPaymentsV3.CounterpartAccountGenericIdentificationScheme(
                                    proprietary = Some "BBAN"
                                    )
                    //,"issuer"
                ))
    | UK_Domestic(sortcode, account) ->
        let identifier =
            "GBR" +
                sortcode.Replace(" ", "").Replace("-", "") +
                account.Replace(" ", "").Replace("-", "")
        FpsPaymentsV3.BatchPaymentInstructionCounterpartAccountIdentification(
            //iban = "iban",
            other =
                FpsPaymentsV3.BatchCounterpartAccountGenericIdentification(
                    identifier,
                    schemeName = FpsPaymentsV3.CounterpartAccountGenericIdentificationScheme(
                                    proprietary = Some "PRTY_COUNTRY_SPECIFIC"
                                    )
                    //,"issuer"
                ))

type PaymentTransfer = {
    To: BankAccount
    AccountHolder: string
    Sum: decimal
    Currency: string
    Description: string
    PaymentReference: string
    TransactionId: string
}

/// Creates credit transfer for createPaymentInstruction
let createCreditTransfer (payment:PaymentTransfer) =
    FpsPaymentsV3.BatchCreditTransfer(
        paymentIdentification = FpsPaymentsV3.BatchPaymentIdentification(
                                    payment.Description, // instructionIdentification
                                    payment.TransactionId // endToEndIdentification
                                ),

        amount = FpsPaymentsV3.BatchAmount(Convert.ToDouble(payment.Sum), Some payment.Currency),
        creditor = FpsPaymentsV3.BatchCreditorPartyIdentifier(payment.AccountHolder (*, "legalEntityIdentifier"*)),
        creditorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
            identification = ``account to string`` payment.To),
        remittanceInformation =
            FpsPaymentsV3.BatchRemittanceInformation(structured =
                FpsPaymentsV3.BatchStructured(creditorReferenceInformation =
                    FpsPaymentsV3.BatchCreditorReferenceInformation(Some payment.PaymentReference // reference

                )
            )
        )
    )

/// Creates payment instructions from createCreditTransfer for transferPayments
let createPaymentInstruction address legalEntityIdentifier batchId (account:ClearBank.Common.BankAccount) transfers =
    let req =
        FpsPaymentsV3.BatchPaymentInstruction(
            debtor = FpsPaymentsV3.BatchDebtorPartyIdentifier(
                address = address,
                legalEntityIdentifier = Some(legalEntityIdentifier |> Option.defaultValue "")),
            paymentInstructionIdentification = batchId,
            debtorAccount = FpsPaymentsV3.BatchPaymentInstructionCounterpartAccount(
                identification = ``account to string`` account
                ),
            creditTransfers = transfers
        )
    req

/// A real account, or a virtual account under its real account.
[<RequireQualifiedAccess>]
type AccountRef =
    | Real of AccountId: Guid
    | Virtual of AccountId: Guid * VirtualAccountId: Guid

[<RequireQualifiedAccess>]
type AccountStatus =
    | Enabled
    | Closed
    | Suspended

/// Why an account is closed or suspended.
[<RequireQualifiedAccess>]
type AccountStatusReason =
    | AccountHolderBankrupt
    | AccountHolderDeceased
    | AccountSwitched
    | CompanyNoLongerTrading
    | DissatisfiedCustomer
    | DuplicateSoleTraderAccount
    | FinancialCrime
    | FraudFirstParty
    | FraudThirdParty
    | FraudConfirmed
    | InternallyDormant
    | KYCRequired
    | LegallyDisputed
    | PotentialSanctionedIndividual
    | SanctionedIndividual
    | SuspectMoneyLaundering
    | TransactionDispute
    | Other
    /// A reason newer than this library.
    | Unlisted of string

[<RequireQualifiedAccess>]
type LegalOwnerType =
    | Personal
    | Business

[<RequireQualifiedAccess>]
type RelationshipType =
    | Single
    | Joint

/// Changes to a real account; ValueNone leaves a field as it is.
type AccountUpdate =
    { Status: AccountStatus voption
      /// Required when closing or suspending; not allowed when enabling.
      StatusReason: AccountStatusReason voption
      OwnerName: string voption
      LegalOwnerType: LegalOwnerType voption
      RelationshipType: RelationshipType voption
      /// Negative for an overdraft; only for embedded banking clients with an overdraft agreement.
      MinimumBalance: decimal voption }

module AccountUpdate =
    let empty =
        { Status = ValueNone
          StatusReason = ValueNone
          OwnerName = ValueNone
          LegalOwnerType = ValueNone
          RelationshipType = ValueNone
          MinimumBalance = ValueNone }

/// Changes to a virtual account; ValueNone leaves a field as it is.
type VirtualAccountUpdate =
    { OwnerName: string voption
      LegalOwnerType: LegalOwnerType voption
      RelationshipType: RelationshipType voption }

module VirtualAccountUpdate =
    let empty =
        { OwnerName = ValueNone
          LegalOwnerType = ValueNone
          RelationshipType = ValueNone }

type NewVirtualAccount =
    { OwnerName: string
      /// Your own identifier for the virtual account.
      ExternalIdentifier: string voption }

/// Filters for Bacs unpaid Direct Debits and unapplied Direct Credits.
type BacsItemQuery =
    { ReasonCode: string voption
      ServiceUserNumber: string voption
      PaymentReference: string voption
      ProcessingDateFrom: DateTimeOffset voption
      ProcessingDateTo: DateTimeOffset voption
      Amount: decimal voption
      TransactionCodes: string list }

module BacsItemQuery =
    let empty =
        { ReasonCode = ValueNone
          ServiceUserNumber = ValueNone
          PaymentReference = ValueNone
          ProcessingDateFrom = ValueNone
          ProcessingDateTo = ValueNone
          Amount = ValueNone
          TransactionCodes = [] }

/// A Confirmation of Payee name check before paying a UK account.
type PayeeCheck =
    { SortCode: string
      AccountNumber: string
      /// Comma-delimited as ClearBank expects; ",,Full Name" is the unstructured form.
      OwnerName: string
      LegalOwnerType: LegalOwnerType
      /// The payment reference, for accounts that need Secondary Reference Data.
      SecondaryReference: string voption
      /// Links later payments to this check.
      EndToEndId: string voption }

type PayeeCheckResult =
    { Matched: bool
      /// The account name, when the responder returned it (close matches).
      Name: string voption
      ReasonCode: string voption
      ReasonCodeDescription: string voption
      MatchedBank: string voption
      ResponseWithinSla: bool voption
      LegalOwnerType: string voption
      ResponderRegistrationId: string voption }

type SecondaryReferenceCheck =
    { Required: bool
      BankName: string voption }

/// Whether an account answers inbound Confirmation of Payee checks.
[<RequireQualifiedAccess>]
type CopParticipation =
    | Participate
    | OptOut of Reason: string

module internal Wire =

    let accountStatus =
        function
        | AccountStatus.Enabled -> "Enabled"
        | AccountStatus.Closed -> "Closed"
        | AccountStatus.Suspended -> "Suspended"

    let statusReason =
        function
        | AccountStatusReason.AccountHolderBankrupt -> "AccountHolderBankrupt"
        | AccountStatusReason.AccountHolderDeceased -> "AccountHolderDeceased"
        | AccountStatusReason.AccountSwitched -> "AccountSwitched"
        | AccountStatusReason.CompanyNoLongerTrading -> "CompanyNoLongerTrading"
        | AccountStatusReason.DissatisfiedCustomer -> "DissatisfiedCustomer"
        | AccountStatusReason.DuplicateSoleTraderAccount -> "DuplicateSoleTraderAccount"
        | AccountStatusReason.FinancialCrime -> "FinancialCrime"
        | AccountStatusReason.FraudFirstParty -> "FraudFirstParty"
        | AccountStatusReason.FraudThirdParty -> "FraudThirdParty"
        | AccountStatusReason.FraudConfirmed -> "FraudConfirmed"
        | AccountStatusReason.InternallyDormant -> "InternallyDormant"
        | AccountStatusReason.KYCRequired -> "KYCRequired"
        | AccountStatusReason.LegallyDisputed -> "LegallyDisputed"
        | AccountStatusReason.PotentialSanctionedIndividual -> "PotentialSanctionedIndividual"
        | AccountStatusReason.SanctionedIndividual -> "SanctionedIndividual"
        | AccountStatusReason.SuspectMoneyLaundering -> "SuspectMoneyLaundering"
        | AccountStatusReason.TransactionDispute -> "TransactionDispute"
        | AccountStatusReason.Other -> "Other"
        | AccountStatusReason.Unlisted name -> name

    let legalOwnerType =
        function
        | LegalOwnerType.Personal -> "Personal"
        | LegalOwnerType.Business -> "Business"

    let relationshipType =
        function
        | RelationshipType.Single -> "Single"
        | RelationshipType.Joint -> "Joint"

    let private digits (text: string) = text |> String.filter Char.IsDigit

    /// Sort code and account number as one identification, the account number zero-padded to 8 digits.
    let ukIdentification (sortCode: string) (accountNumber: string) =
        digits sortCode + (digits accountNumber).PadLeft(8, '0')

    let private some (value: 'T voption) = ValueOption.toOption value

    let accountUpdate (update: AccountUpdate) =
        SterlingModels.Binding.Accounts.PatchAccountRequest(
            status = some (update.Status |> ValueOption.map accountStatus),
            statusReason = some (update.StatusReason |> ValueOption.map statusReason),
            ownerName = some update.OwnerName,
            legalOwnerType = some (update.LegalOwnerType |> ValueOption.map legalOwnerType),
            relationshipType = some (update.RelationshipType |> ValueOption.map relationshipType),
            minimumBalance = some (update.MinimumBalance |> ValueOption.map float))

    let virtualAccountUpdate (update: VirtualAccountUpdate) =
        SterlingModels.Binding.VirtualAccounts.UpdateVirtualAccountRequest(
            ownerName = some update.OwnerName,
            legalOwnerType = some (update.LegalOwnerType |> ValueOption.map legalOwnerType),
            relationshipType = some (update.RelationshipType |> ValueOption.map relationshipType))

    let payeeCheck (check: PayeeCheck) =
        CopOutboundV1.CoPOutboundRequest(
            "SortCodeAccountNumber",
            legalOwnerType check.LegalOwnerType,
            ukIdentification check.SortCode check.AccountNumber,
            check.OwnerName,
            some check.SecondaryReference,
            some check.EndToEndId)

    let payeeCheckResult (response: CopOutboundV1.CoPOutboundResponse) =
        match response |> Option.ofObj |> Option.bind (_.Data >> Option.ofObj) |> Option.bind (_.VerificationReport >> Option.ofObj) with
        | Some report ->
            { Matched = report.Matched
              Name = ValueOption.ofOption report.Name
              ReasonCode = ValueOption.ofOption report.ReasonCode
              ReasonCodeDescription = ValueOption.ofOption report.ReasonCodeDescription
              MatchedBank = ValueOption.ofObj report.MatchedBank
              ResponseWithinSla = ValueOption.ofOption report.ResponseWithinSla
              LegalOwnerType = ValueOption.ofOption report.LegalOwnerType
              ResponderRegistrationId = ValueOption.ofOption report.ResponderRegistrationId }
        | None -> failwith "The response has no Data.VerificationReport object."

    let secondaryReferenceCheck (response: CopOutboundV1.SRDValidationResponse) =
        match response |> Option.ofObj |> Option.bind (_.Data >> Option.ofObj) with
        | Some data ->
            { Required = data.Required |> Option.defaultValue false
              BankName = ValueOption.ofOption data.BankName }
        | None -> failwith "The response has no Data object."

    let copParticipation =
        function
        | CopParticipation.Participate -> CopAccountsV1.OptRequest(false, None)
        | CopParticipation.OptOut reason -> CopAccountsV1.OptRequest(true, Some reason)

/// A CHAPS v6 source, debtor or creditor account: an IBAN, a BBAN, or a UK sort code and account number.
let chapsAccount (account: BankAccount) : obj =
    match account with
    | IBAN iban -> box (ChapsV6.AccountIdentificationIbanDtoV6 iban)
    | BBAN bban -> box (ChapsV6.AccountIdentificationOtherDtoV6("BBan", bban))
    | UK_Domestic(sortCode, accountNumber) ->
        box (ChapsV6.AccountIdentificationOtherDtoV6("SortcodeAccountNumber", Wire.ukIdentification sortCode accountNumber))

/// Task-based operations over a shared ClearBankClient. Failures are Error (exception, response body).
module Api =

    let private v1 (client: ClearBankClient) = ClearBankSwaggerV1.Client client.Http
    let private v2 (client: ClearBankClient) = ClearBankSwaggerV2.Client client.Http
    let private v3 (client: ClearBankClient) = ClearBankOpenApiV3Accounts.Client client.Http
    let private paymentOptions =
        let options = JsonSerializerOptions()
        options.Converters.Add(TwoDecimalsConverter())
        options

    /// Leaves out unset optional fields, so a PATCH changes only what it names.
    let private omitUnset = JsonSerializerOptions(DefaultIgnoreCondition = Serialization.JsonIgnoreCondition.WhenWritingNull)

    let private chapsOptions =
        let options = JsonSerializerOptions(DefaultIgnoreCondition = Serialization.JsonIgnoreCondition.WhenWritingNull)
        options.Converters.Add(TwoDecimalsConverter())
        options

    let private sterling (client: ClearBankClient) = SterlingV1.Client(client.Http, omitUnset)
    let private copOutbound (client: ClearBankClient) = CopOutboundV1.Client(client.Http, omitUnset)
    let private copAccounts (client: ClearBankClient) = CopAccountsV1.Client(client.Http, omitUnset)
    let private chaps (client: ClearBankClient) = ChapsV6.Client(client.Http, chapsOptions)

    /// Calls the signed test endpoint.
    let testAsync (client: ClearBankClient) (requestId: Guid) cancellationToken =
        let api = v1 client
        let payload = JsonDocument.Parse("""{"institutionId": "string","body": "hello world!"}""") |> box
        signed client (fun () -> api.Serialize payload) cancellationToken (fun signature ->
            api.V1TestPost(client.Authorization, signature, requestIdText requestId, payload))

    // Accounts

    /// Creates a real account (v3).
    let createAccountAsync (client: ClearBankClient) (requestId: Guid) (sortCode: string) (accountName: string) (ownerName: string) cancellationToken =
        let api = v3 client
        let request = AccountsV3.CreateAccountRequest(accountName, AccountsV3.PartyIdentification(ownerName), sortCode.Replace("-", ""))
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.V3InstitutionsByInstitutionIdAccountsPost(client.Authorization, signature, requestIdText requestId, request, cancellationToken))

    /// Lists the real accounts with their balances (v3).
    let getAccountsAsync (client: ClearBankClient) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        (v3 client).V3InstitutionsByInstitutionIdAccountsGet(client.Authorization, ValueOption.toOption pageNumber, ValueOption.toOption pageSize, cancellationToken)
        |> attempt cancellationToken

    /// Gets a real account with its balances (v3).
    let getAccountAsync (client: ClearBankClient) (accountId: Guid) cancellationToken =
        (v3 client).V3InstitutionsByInstitutionIdAccountsByAccountIdGet(accountId, client.Authorization, cancellationToken)
        |> attempt cancellationToken

    /// Changes a real account's status, owner or minimum balance.
    let updateAccountAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (update: AccountUpdate) cancellationToken =
        let api = sterling client
        let request = Wire.accountUpdate update
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.V1InstitutionsByInstitutionIdAccountsByAccountIdPatch(accountId, client.Authorization, signature, requestIdText requestId, request, cancellationToken))

    /// Closes a real account by setting its status to Closed.
    let closeAccountAsync client requestId accountId (reason: AccountStatusReason) cancellationToken =
        updateAccountAsync client requestId accountId
            { AccountUpdate.empty with Status = ValueSome AccountStatus.Closed; StatusReason = ValueSome reason }
            cancellationToken

    /// Requests closure of an interest-bearing account (savings, ISA); accrued interest goes to its nominated account.
    /// Closure is asynchronous: an AccountClosureCompleted or AccountClosureFailed webhook follows.
    let requestAccountClosureAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (reason: AccountStatusReason) cancellationToken =
        let api = sterling client
        let request = SterlingModels.Binding.Accounts.AccountClosureRequest(Wire.statusReason reason)
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.V1InstitutionsByInstitutionIdAccountsByAccountIdClosurePost(accountId, client.Authorization, signature, requestIdText requestId, request, cancellationToken))

    /// Gets the closure status of an account: its accountStatus, closureStatus and any closureFailureReasons.
    let getAccountClosureAsync (client: ClearBankClient) (accountId: Guid) cancellationToken =
        (sterling client).V1InstitutionsByInstitutionIdAccountsByAccountIdClosureGet(accountId, client.Authorization, cancellationToken)
        |> attempt cancellationToken

    // Transactions

    /// Lists the institution's transactions (v2).
    let getTransactionsAsync (client: ClearBankClient) (pageNumber: int voption) (pageSize: int voption) (startDate: DateTimeOffset voption) (endDate: DateTimeOffset voption) cancellationToken =
        (v2 client).V2TransactionsGet(client.Authorization, ValueOption.toOption pageNumber, ValueOption.toOption pageSize, ValueOption.toOption startDate, ValueOption.toOption endDate)
        |> attempt cancellationToken

    /// Lists a real or virtual account's transactions (v2).
    let getAccountTransactionsAsync (client: ClearBankClient) (account: AccountRef) (pageNumber: int voption) (pageSize: int voption) (startDate: DateTimeOffset voption) (endDate: DateTimeOffset voption) cancellationToken =
        let api = v2 client
        let page, size, from, until = ValueOption.toOption pageNumber, ValueOption.toOption pageSize, ValueOption.toOption startDate, ValueOption.toOption endDate

        match account with
        | AccountRef.Real accountId ->
            api.V2AccountsByAccountIdTransactionsGet(accountId, client.Authorization, page, size, from, until)
        | AccountRef.Virtual(accountId, virtualAccountId) ->
            api.V2AccountsByAccountIdVirtualByVirtualAccountIdTransactionsGet(accountId, virtualAccountId, client.Authorization, page, size, from, until)
        |> attempt cancellationToken

    /// Gets one transaction of a real or virtual account (v2).
    let getAccountTransactionAsync (client: ClearBankClient) (account: AccountRef) (transactionId: Guid) cancellationToken =
        let api = v2 client

        match account with
        | AccountRef.Real accountId ->
            api.V2AccountsByAccountIdTransactionsByTransactionIdGet(accountId, transactionId, client.Authorization)
        | AccountRef.Virtual(accountId, virtualAccountId) ->
            api.V2AccountsByAccountIdVirtualByVirtualAccountIdTransactionsByTransactionIdGet(accountId, virtualAccountId, transactionId, client.Authorization)
        |> attempt cancellationToken

    /// Returns Bacs Direct Debit or Direct Credit transactions as (transaction id, reason code); valid on the entry day,
    /// or the next working day before 15:30 UTC.
    let returnTransactionsAsync (client: ClearBankClient) (requestId: Guid) (account: AccountRef) (returns: (Guid * string) seq) cancellationToken =
        let api = v1 client

        let request =
            returns
            |> Seq.map (fun (transactionId, reasonCode) -> V1Models.Binding.Mandates.TransactionReturn(transactionId, reasonCode))
            |> Seq.toArray
            |> V1Models.Binding.Mandates.TransactionReturnsRequest

        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            match account with
            | AccountRef.Real accountId ->
                api.V1AccountsByAccountIdTransactionsReturnsPost(accountId, client.Authorization, signature, requestIdText requestId, request)
            | AccountRef.Virtual(accountId, virtualAccountId) ->
                api.V1AccountsByAccountIdVirtualByVirtualAccountIdTransactionsReturnsPost(accountId, virtualAccountId, client.Authorization, signature, requestIdText requestId, request))

    /// Lists unpaid Bacs Direct Debits for a sort code.
    let getUnpaidDirectDebitsAsync (client: ClearBankClient) (sortCode: string) (query: BacsItemQuery) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        (v1 client).V1TransactionsBySortCodeDirectDebitUnpaidGet(
            sortCode.Replace("-", ""), client.Authorization, ValueOption.toOption query.ReasonCode, ValueOption.toOption query.ServiceUserNumber, ValueOption.toOption query.PaymentReference,
            ValueOption.toOption query.ProcessingDateFrom, ValueOption.toOption query.ProcessingDateTo,
            query.Amount |> ValueOption.map float |> ValueOption.toOption, List.toArray query.TransactionCodes,
            ValueOption.toOption pageNumber, ValueOption.toOption pageSize)
        |> attempt cancellationToken

    /// Lists unapplied Bacs Direct Credits for a sort code.
    let getUnappliedDirectCreditsAsync (client: ClearBankClient) (sortCode: string) (query: BacsItemQuery) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        (v1 client).V1TransactionsBySortCodeDirectCreditUnappliedGet(
            sortCode.Replace("-", ""), client.Authorization, ValueOption.toOption query.ReasonCode, ValueOption.toOption query.ServiceUserNumber, ValueOption.toOption query.PaymentReference,
            ValueOption.toOption query.ProcessingDateFrom, ValueOption.toOption query.ProcessingDateTo,
            query.Amount |> ValueOption.map float |> ValueOption.toOption, List.toArray query.TransactionCodes,
            ValueOption.toOption pageNumber, ValueOption.toOption pageSize)
        |> attempt cancellationToken

    // Virtual accounts

    /// Requests virtual accounts under a real account (v2); a VirtualAccountCreated webhook follows each.
    let createVirtualAccountsAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (accounts: NewVirtualAccount seq) cancellationToken =
        let api = v2 client

        let request =
            accounts
            |> Seq.map (fun account ->
                V2Models.Binding.VirtualAccounts.CreateVirtualAccount(
                    account.OwnerName,
                    V2Models.VirtualAccountIdentifier(externalIdentifier = ValueOption.toOption account.ExternalIdentifier)))
            |> Seq.toArray
            |> V2Models.Binding.VirtualAccounts.CreateVirtualAccountsRequest

        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.V2AccountsByAccountIdVirtualPost(accountId, client.Authorization, signature, requestIdText requestId, request))

    /// Lists the virtual accounts under a real account (v2).
    let getVirtualAccountsAsync (client: ClearBankClient) (accountId: Guid) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        (v2 client).V2AccountsByAccountIdVirtualGet(accountId, client.Authorization, ValueOption.toOption pageNumber, ValueOption.toOption pageSize)
        |> attempt cancellationToken

    /// Lists every virtual account of the institution (v1).
    let getAllVirtualAccountsAsync (client: ClearBankClient) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        (v1 client).V1AccountsVirtualGet(client.Authorization, ValueOption.toOption pageNumber, ValueOption.toOption pageSize)
        |> attempt cancellationToken

    /// Gets a virtual account (v2).
    let getVirtualAccountAsync (client: ClearBankClient) (accountId: Guid) (virtualAccountId: Guid) cancellationToken =
        (v2 client).V2AccountsByAccountIdVirtualByVirtualAccountIdGet(accountId, virtualAccountId, client.Authorization)
        |> attempt cancellationToken

    /// Changes a virtual account's owner details.
    let updateVirtualAccountAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (virtualAccountId: Guid) (update: VirtualAccountUpdate) cancellationToken =
        let api = sterling client
        let request = Wire.virtualAccountUpdate update
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.V1InstitutionsByInstitutionIdAccountsByAccountIdVirtualByVirtualAccountIdPatch(
                accountId, virtualAccountId, client.Authorization, signature, requestIdText requestId, request, cancellationToken))

    /// Disables a virtual account.
    let disableVirtualAccountAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (virtualAccountId: Guid) cancellationToken =
        (v1 client).V1AccountsByAccountIdVirtualByVirtualAccountIdDelete(accountId, virtualAccountId, client.Authorization, requestIdText requestId)
        |> attempt cancellationToken

    // Bacs Direct Debit mandates

    /// Lists the Direct Debit mandates of a real or virtual account (v2).
    let getMandatesAsync (client: ClearBankClient) (account: AccountRef) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        let api = v2 client
        let page, size = ValueOption.toOption pageNumber, ValueOption.toOption pageSize

        match account with
        | AccountRef.Real accountId -> api.V2AccountsByAccountIdMandatesGet(accountId, client.Authorization, page, size)
        | AccountRef.Virtual(accountId, virtualAccountId) ->
            api.V2AccountsByAccountIdVirtualByVirtualAccountIdMandatesGet(accountId, virtualAccountId, client.Authorization, page, size)
        |> attempt cancellationToken

    /// Cancels a Direct Debit mandate with a Bacs reason code.
    let cancelMandateAsync (client: ClearBankClient) (requestId: Guid) (account: AccountRef) (mandateId: Guid) (reasonCode: string) cancellationToken =
        let api = v1 client
        let request = V1Models.Binding.Mandates.MandateCancellationRequest(reasonCode)

        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            match account with
            | AccountRef.Real accountId ->
                api.V1AccountsByAccountIdMandatesByMandateIdDelete(accountId, mandateId, client.Authorization, signature, requestIdText requestId, request)
            | AccountRef.Virtual(accountId, virtualAccountId) ->
                api.V1AccountsByAccountIdVirtualByVirtualAccountIdMandatesByMandateIdDelete(
                    accountId, virtualAccountId, mandateId, client.Authorization, signature, requestIdText requestId, request))

    /// Rejects (returns) a Direct Debit mandate.
    let returnMandateAsync (client: ClearBankClient) (requestId: Guid) (account: AccountRef) (mandateId: Guid) (rejectionReason: string) cancellationToken =
        let api = v1 client
        let request = V1Models.Binding.Mandates.DirectDebitMandateRejectRequest(rejectionReason)

        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            match account with
            | AccountRef.Real accountId ->
                api.V1AccountsByAccountIdMandatesByMandateIdReturnsPost(accountId, mandateId, client.Authorization, signature, requestIdText requestId, request)
            | AccountRef.Virtual(accountId, virtualAccountId) ->
                api.V1AccountsByAccountIdVirtualByVirtualAccountIdMandatesByMandateIdReturnsPost(
                    accountId, virtualAccountId, mandateId, client.Authorization, signature, requestIdText requestId, request))

    /// Lists the collections taken under a mandate (v2).
    let getMandateCollectionsAsync (client: ClearBankClient) (account: AccountRef) (mandateId: Guid) (startDate: DateTimeOffset voption) (endDate: DateTimeOffset voption) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        let api = v2 client
        let from, until, page, size = ValueOption.toOption startDate, ValueOption.toOption endDate, ValueOption.toOption pageNumber, ValueOption.toOption pageSize

        match account with
        | AccountRef.Real accountId ->
            api.V2AccountsByAccountIdMandatesByMandateIdCollectionsGet(accountId, mandateId, client.Authorization, from, until, page, size)
        | AccountRef.Virtual(accountId, virtualAccountId) ->
            api.V2AccountsByAccountIdVirtualByVirtualAccountIdMandatesByMandateIdCollectionsGet(
                accountId, virtualAccountId, mandateId, client.Authorization, from, until, page, size)
        |> attempt cancellationToken

    /// Lists unpaid Direct Debit collections of a real or virtual account (v2).
    let getUnpaidCollectionsAsync (client: ClearBankClient) (account: AccountRef) (startDate: DateTimeOffset voption) (endDate: DateTimeOffset voption) (serviceUserNumber: string voption) (reference: string voption) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
        let api = v2 client
        let from, until, page, size = ValueOption.toOption startDate, ValueOption.toOption endDate, ValueOption.toOption pageNumber, ValueOption.toOption pageSize
        let sun, ref = ValueOption.toOption serviceUserNumber, ValueOption.toOption reference

        match account with
        | AccountRef.Real accountId ->
            api.V2AccountsByAccountIdMandatesUnpaidGet(accountId, client.Authorization, from, until, sun, ref, page, size)
        | AccountRef.Virtual(accountId, virtualAccountId) ->
            api.V2AccountsByAccountIdVirtualByVirtualAccountIdMandatesUnpaidGet(accountId, virtualAccountId, client.Authorization, from, until, sun, ref, page, size)
        |> attempt cancellationToken

    // Payments

    /// Posts Faster Payments created with createPaymentInstruction (v3). Payments to ClearBank-held accounts settle as internal transfers.
    let transferPaymentsAsync (client: ClearBankClient) (requestId: Guid) paymentInstructions cancellationToken =
        let api = FpsPaymentsV3.Client(client.Http, paymentOptions)
        let request = FpsPaymentsV3.BatchCreateCreditTransferRequest(paymentInstructions = paymentInstructions)
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            api.Post(client.Authorization, signature, requestIdText requestId, request, cancellationToken))

    // Confirmation of Payee

    /// Checks a payee's name against the account before paying (outbound Confirmation of Payee).
    let verifyPayeeAsync (client: ClearBankClient) (requestId: Guid) (check: PayeeCheck) cancellationToken =
        let api = copOutbound client
        let request = Wire.payeeCheck check
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            async {
                let! response = api.PostOpenBankingOutboundV1NameVerification(client.Authorization, signature, requestIdText requestId, request, cancellationToken)
                return Wire.payeeCheckResult response
            })

    /// Checks whether an account needs Secondary Reference Data (a roll number or reference) in payments.
    let requiresSecondaryReferenceAsync (client: ClearBankClient) (requestId: Guid) (sortCode: string) (accountNumber: string) cancellationToken =
        let api = copOutbound client
        let request = CopOutboundV1.SRDValidationRequest("SortCodeAccountNumber", Wire.ukIdentification sortCode accountNumber)
        signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
            async {
                let! response = api.PostOpenBankingOutboundV1SrdValidate(client.Authorization, signature, requestIdText requestId, request, cancellationToken)
                return Wire.secondaryReferenceCheck response
            })

    /// Opts a real or virtual account in or out of answering inbound Confirmation of Payee checks.
    /// Sent untyped: the endpoint answers 200 with a body or 204 without one, and the generated client cannot read an empty 204.
    let setCopParticipationAsync (client: ClearBankClient) (requestId: Guid) (account: AccountRef) (participation: CopParticipation) cancellationToken =
        let body = (copAccounts client).Serialize(Wire.copParticipation participation)

        let path =
            match account with
            | AccountRef.Real accountId -> $"v1/Cop/opt/accounts/{idText accountId}"
            | AccountRef.Virtual(accountId, virtualAccountId) -> $"v1/Cop/opt/accounts/{idText accountId}/virtual/{idText virtualAccountId}"

        sendJson client HttpMethod.Put path requestId (ValueSome body) cancellationToken
        |> parseWith ignore

    // CHAPS

    /// Posts a CHAPS customer payment (pacs.008) created with ChapsV6.CreateCustomerPaymentDtoV6; accounts from chapsAccount.
    let chapsCustomerPaymentAsync (client: ClearBankClient) (requestId: Guid) (payment: ChapsV6.CreateCustomerPaymentDtoV6) cancellationToken =
        let api = chaps client
        signed client (fun () -> api.Serialize payment) cancellationToken (fun signature ->
            api.ExternalCreateCustomerPaymentV6(client.Authorization, signature, requestIdText requestId, payment, cancellationToken))

    /// Posts a CHAPS bank-to-bank payment (pacs.009) created with ChapsV6.CreateInstitutionPaymentDtoV6; authorised institutions only.
    let chapsInstitutionPaymentAsync (client: ClearBankClient) (requestId: Guid) (payment: ChapsV6.CreateInstitutionPaymentDtoV6) cancellationToken =
        let api = chaps client
        signed client (fun () -> api.Serialize payment) cancellationToken (fun signature ->
            api.ExternalCreateInstitutionPaymentV6(client.Authorization, signature, requestIdText requestId, payment, cancellationToken))

    /// Returns a received CHAPS payment (pacs.004) with ChapsV6.CreateReturnPaymentDtoV6.
    let chapsReturnPaymentAsync (client: ClearBankClient) (requestId: Guid) (paymentReturn: ChapsV6.CreateReturnPaymentDtoV6) cancellationToken =
        let api = chaps client
        signed client (fun () -> api.Serialize paymentReturn) cancellationToken (fun signature ->
            api.ExternalReturnPaymentV6(client.Authorization, signature, requestIdText requestId, paymentReturn, cancellationToken))

let private keyVaultSigner = RequestSigner.fromConfig

let callTestEndpoint config azureKeyVaultCertificateName =
    async {
        let requestId = Guid.NewGuid()
        return! perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct -> Api.testAsync client requestId ct)
    }

/// Creates a new account
let createNewAccount config azureKeyVaultCertificateName (requestId:Guid) (sortCode:string) accountName ownerName =
    perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
        Api.createAccountAsync client requestId sortCode accountName ownerName ct)

/// Get all the accounts
let getAccounts config =
    perCall config RequestSigner.unavailable None (fun client ct -> Api.getAccountsAsync client ValueNone ValueNone ct)

/// Get all the transactions
let getTransactions config pageSize pageNumber startDate endDate =
    perCall config RequestSigner.unavailable None (fun client ct ->
        let instant (date: DateTime option) = date |> Option.map asOffset |> ValueOption.ofOption
        Api.getTransactionsAsync client (ValueOption.ofOption pageNumber) (ValueOption.ofOption pageSize) (instant startDate) (instant endDate) ct)

/// Get a transaction with correct end-to-end-id
/// if you are lucky enough to own accountId (from getAccounts) and transactionId (from getTransactions)
let getAccountTransaction config (accountId:string) (transactionId:string) =
    perCall config RequestSigner.unavailable None (fun client ct ->
        attempt ct (async {
            let account, transaction = Guid.Parse accountId, Guid.Parse transactionId
            return! (ClearBankSwaggerV2.Client client.Http).V2AccountsByAccountIdTransactionsByTransactionIdGet(account, transaction, client.Authorization, ct)
        }))

/// Post payments created with createPaymentInstruction
let transferPayments config azureKeyVaultCertificateName (requestId:Guid) paymentInstructions =
    perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
        Api.transferPaymentsAsync client requestId paymentInstructions ct)

module MultiCurrency =

    let [<Literal>]schemaMultiCurrencyTransactions = __SOURCE_DIRECTORY__ + @"/Schemas/mccy-transactions-v1.json"
    let [<Literal>]schemaMultiCurrencyPayments = __SOURCE_DIRECTORY__ + @"/Schemas/mccy-initiate-payment-v1-dec21.json"

    type MccyTransactionsV1 = OpenApiClientProvider<schemaMultiCurrencyTransactions, PreferAsync=true>
    type MccyPaymentsV1 = OpenApiClientProvider<schemaMultiCurrencyPayments, PreferAsync=true>

    [<RequireQualifiedAccess>]
    type AccountKind = GeneralSegregated | DesignatedSegregated | GeneralClient | DesignatedClient | YourFunds
    [<RequireQualifiedAccess>]
    type MccyRoutingCode =
    | IBAN of string
    | BBAN of string
    | Descriptor of string

    let ISOCurrencySymbols() =
        System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
        |> Array.map(fun ci -> ci.Name) |> Array.distinct |> Array.map (System.Globalization.RegionInfo >> fun g -> g.ISOCurrencySymbol)
        |> Array.distinct

    /// Task-based multi-currency operations over a shared ClearBankClient. Failures are Error (exception, response body).
    module Api =

        let private transactions (client: ClearBankClient) = MccyTransactionsV1.Client client.Http

        let private paymentOptions =
            let options = JsonSerializerOptions()
            options.Converters.Add(TwoDecimalsConverter())
            options

        /// Creates a multi-currency account.
        let createAccountAsync (client: ClearBankClient) (requestId: Guid) (sortcode: string) accountName ownerName (accountKind: AccountKind) isoCurrencySymbols identifiers productId customerId cancellationToken =
            let api = transactions client
            let routingCode = MccyTransactionsV1.RoutingCode(sortcode.Replace("-", "").Replace(" ", ""), "GBSortCode")
            let request = MccyTransactionsV1.CreateAccountRequest(accountName, ownerName, (accountKind.ToString()), isoCurrencySymbols, routingCode, identifiers, productId, customerId)
            signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
                api.PostMccyV1Accounts(client.Authorization, signature, requestIdText requestId, request, cancellationToken))

        /// Lists the multi-currency accounts.
        let getAccountsAsync (client: ClearBankClient) (pageNumber: int voption) (pageSize: int voption) cancellationToken =
            (transactions client).GetMccyV1Accounts(client.Authorization, ValueOption.toOption pageNumber, ValueOption.toOption pageSize, cancellationToken)
            |> attempt cancellationToken

        /// Lists an account's transactions in one currency.
        let getTransactionsAsync (client: ClearBankClient) (accountId: Guid) (isoCurrencyCode: string) (pageNumber: int voption) (pageSize: int voption) (startDate: DateTimeOffset voption) (endDate: DateTimeOffset voption) cancellationToken =
            (transactions client).GetMccyV1AccountTransactions(
                client.Authorization, accountId, isoCurrencyCode, ValueOption.toOption pageNumber, ValueOption.toOption pageSize,
                ValueOption.toOption startDate, ValueOption.toOption endDate, cancellationToken)
            |> attempt cancellationToken

        /// Gets an account.
        let getAccountAsync (client: ClearBankClient) (accountId: Guid) cancellationToken =
            (transactions client).GetMccyV1Account(client.Authorization, accountId, cancellationToken)
            |> attempt cancellationToken

        /// Gets an account's balances in every currency.
        let getAccountBalancesAsync (client: ClearBankClient) (accountId: Guid) cancellationToken =
            (transactions client).GetMccyV1AccountBalances(accountId, client.Authorization, cancellationToken)
            |> attempt cancellationToken

        /// Gets a transaction.
        let getTransactionAsync (client: ClearBankClient) (transactionId: Guid) cancellationToken =
            (transactions client).GetMccyV1Transaction(transactionId, client.Authorization, cancellationToken)
            |> attempt cancellationToken

        /// Changes an account's details.
        let updateAccountAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (updateRequest: MccyTransactionsV1.UpdateAccountRequest) cancellationToken =
            let api = transactions client
            signed client (fun () -> api.Serialize updateRequest) cancellationToken (fun signature ->
                api.PatchMccyV1Account(accountId, client.Authorization, signature, requestIdText requestId, updateRequest, cancellationToken))

        /// Enables or disables an account.
        let updateAccountStatusAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (statusRequest: MccyTransactionsV1.UpdateAccountStatusRequest) cancellationToken =
            let api = transactions client
            signed client (fun () -> api.Serialize statusRequest) cancellationToken (fun signature ->
                api.PatchMccyV1AccountStatus(accountId, client.Authorization, signature, requestIdText requestId, statusRequest, cancellationToken))

        /// Adds a currency to an account.
        let addCurrencyAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (addCurrencyRequest: MccyTransactionsV1.AddCurrencyRequest) cancellationToken =
            let api = transactions client
            signed client (fun () -> api.Serialize addCurrencyRequest) cancellationToken (fun signature ->
                api.PostMccyV1AccountCurrencies(accountId, client.Authorization, signature, requestIdText requestId, addCurrencyRequest, cancellationToken))

        /// Closes an account. Reasons: Other, AccountHolderDeceased, AccountSwitched, CompanyNoLongerTrading, DuplicateAccount.
        let closeAccountAsync (client: ClearBankClient) (requestId: Guid) (accountId: Guid) (closeReason: string voption) cancellationToken =
            let query =
                closeReason
                |> ValueOption.map (fun reason -> "?accountCloseReason=" + Uri.EscapeDataString reason)
                |> ValueOption.defaultValue ""

            sendJson client HttpMethod.Delete $"mccy/v1/Accounts/{idText accountId}{query}" requestId ValueNone cancellationToken
            |> parseWith ignore

        /// Posts payments created with MccyPaymentsV1.PaymentRequestItem.
        let transferPaymentsAsync (client: ClearBankClient) (requestId: Guid) batchId isoCurrencyCode paymentInstructions cancellationToken =
            let api = MccyPaymentsV1.Client(client.Http, paymentOptions)
            let request = MccyPaymentsV1.PaymentRequest(isoCurrencyCode, paymentInstructions, batchId)
            signed client (fun () -> api.Serialize request) cancellationToken (fun signature ->
                api.PostV1MccyPayments(client.Authorization, signature, requestIdText requestId, request, cancellationToken))

        /// Cancels a payment batch, or one payment of it by end-to-end id, while it is still cancellable.
        let cancelPaymentsAsync (client: ClearBankClient) (requestId: Guid) (batchId: string) (endToEndId: string voption) cancellationToken =
            let api = MccyPaymentsV1.Client(client.Http, paymentOptions)

            match endToEndId with
            | ValueSome pid -> api.DeleteV1MccyPayment1(client.Authorization, requestIdText requestId, batchId, pid, cancellationToken)
            | ValueNone -> api.DeleteV1MccyPayment(client.Authorization, requestIdText requestId, batchId, cancellationToken)
            |> attempt cancellationToken

    /// Creates a new account
    let createNewAccount config azureKeyVaultCertificateName (requestId:Guid) (sortcode:string) accountName ownerName (accountKind:AccountKind) isoCurrencySymbols identifiers productId customerId =
        perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
            Api.createAccountAsync client requestId sortcode accountName ownerName accountKind isoCurrencySymbols identifiers productId customerId ct)

    /// Get all the accounts
    let getAccounts config =
        perCall config RequestSigner.unavailable None (fun client ct -> Api.getAccountsAsync client ValueNone ValueNone ct)

    /// Get all the transactions
    let getTransactions config accountId isoCurrecyCode pageSize pageNumber startDate endDate =
        perCall config RequestSigner.unavailable None (fun client ct ->
            Api.getTransactionsAsync client accountId isoCurrecyCode (ValueOption.ofOption pageNumber) (ValueOption.ofOption pageSize) (ValueOption.ofOption startDate) (ValueOption.ofOption endDate) ct)

    /// Get a specific account by ID
    let getAccount config (accountId:Guid) =
        perCall config RequestSigner.unavailable None (fun client ct -> Api.getAccountAsync client accountId ct)

    /// Get account balances for all currencies
    let getAccountBalances config (accountId:Guid) =
        perCall config RequestSigner.unavailable None (fun client ct -> Api.getAccountBalancesAsync client accountId ct)

    /// Get a specific transaction by ID
    let getTransaction config (transactionId:Guid) =
        perCall config RequestSigner.unavailable None (fun client ct -> Api.getTransactionAsync client transactionId ct)

    /// Update account details
    let updateAccount config azureKeyVaultCertificateName (requestId:Guid) (accountId:Guid) updateRequest =
        perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
            Api.updateAccountAsync client requestId accountId updateRequest ct)

    /// Update account status (enable/disable)
    let updateAccountStatus config azureKeyVaultCertificateName (requestId:Guid) (accountId:Guid) statusRequest =
        perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
            Api.updateAccountStatusAsync client requestId accountId statusRequest ct)

    /// Add currency to an existing account
    let addCurrency config azureKeyVaultCertificateName (requestId:Guid) (accountId:Guid) addCurrencyRequest =
        perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
            Api.addCurrencyAsync client requestId accountId addCurrencyRequest ct)

    /// Delete/close an account (optionally with a reason)
    /// Note: DELETE operations don't require signature but do require authentication
    let deleteAccount config (requestId:Guid) (accountId:Guid) (closeReason:string option) =
        perCall config RequestSigner.unavailable (Some requestId) (fun client ct ->
            Api.closeAccountAsync client requestId accountId (ValueOption.ofOption closeReason) ct)


    type InternationalPaymentTransfer = {
        To: BankAccount
        AccountHolder: MccyPaymentsV1.Creditor
        Sum: decimal
        Currency: string
        Description: string
        PaymentReference: string
        TransactionId: string
    }

    /// Post payments created with MccyPaymentsV1.PaymentRequestItem
    let transferPayments config azureKeyVaultCertificateName (requestId:Guid) batchId isoCurrencyCode paymentInstructions =
        perCall config (keyVaultSigner config azureKeyVaultCertificateName) (Some requestId) (fun client ct ->
            Api.transferPaymentsAsync client requestId batchId isoCurrencyCode paymentInstructions ct)

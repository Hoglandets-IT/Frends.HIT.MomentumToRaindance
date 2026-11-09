# Distribution, customer, company, and period-text types

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

## LedgerNoteAccountingDistribution

A ledger note accounting distribution in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note accounting distribution's universally unique identifier. |
| `ledgerNoteAccounting` | `LedgerNoteAccounting` | The ledger note accounting |
| `type` | `LedgerNoteAccountingDistributionType` | The ledger note accounting distribution type |
| `address` | `LedgerNoteAddress` | The ledger note address. This is an e-mail address id it is avalible, otherwise it is the same as the postal address |
| `postalAddress` | `LedgerNoteAddress` | The ledger note postal address |
| `bankCode` | `BankCode` | Bank code. Is only used when type is e-invoice (E-faktura) |
| `identityOfficialNumber` | `String` | Person or company registration number. Is only used when type is e-invoice (E-faktura).<br>If the actor is an organisation this value is the organisation's registration number (orginisationsnummer), otherwise it is a person's registration number (personnummer). |
| `globalLocationNumber` | `String` | The global Location Number. Is only used when type is sve-invoice (Svefaktura) |
| `ordererReference1` | `String` | The first orderer Reference. Is only used when type is sve-invoice (Svefaktura) |
| `ordererReference2` | `String` | The second orderer Reference. Is only used when type is sve-invoice (Svefaktura) |

## LedgerNoteAccountingDistributionType

A ledger note accounting distribution type in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note accounting distribution type's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the ledger note accounting distribution type was removed. Is null if not removed. |
| `displayName` | `String` | Display name of the ledger note accounting distribution type. |
| `shortDisplayName` | `String` | Short display name of the ledger note accounting distribution type. |

## LedgerNoteAddress

A ledger note address in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The ledger note address's universally unique identifier. |
| `address` | `String` | The address |
| `name` | `String` | The name written on the address |
| `attention` | `String` | The attention part of the address |
| `careOf` | `String` | The care of part of the address |
| `streetAddress1` | `String` | The first street address |
| `streetAddress2` | `String` | The second street address |
| `postCode` | `String` | The local postcode where the address is located. |
| `city` | `String` | The city where the address is located. |
| `country` | `Country` | The country where the address is located. |
| `isPostalAddress` | `Boolean` | Indicates if the address is a postal adress or not. If not it is an e-mail address |

## Country

A country in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The country's universally unique identifier. |
| `statusRemoved` | `DateTime` | The date and time that the country was removed. Is null if not removed. |
| `displayName` | `String` | Display name of the country. |
| `shortDisplayName` | `String` | Short display name of the country. |
| `showInAddresses` | `Boolean` | Indicates if the country should be included in addresses. |

## BankCode

A bank code in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The bank code's universally unique identifier. |
| `bankCode` | `String` | The bank code. |
| `displayName` | `String` | Display name of the bank code. |

## Company

A company in the system. E.g. `On1`, `On2b`

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The company's universally unique identifier. |
| `number` | `String` | A distinguished number, assigned to each company in the system. E.g. 100. |
| `organisationNumber` | `String` | A distinguished number, assigned to each company. E.g. 123456-1234. |
| `displayName` | `String` | Display name of the company. E.g. `Mitt Bolag AB`. |
| `nodeType` | `NodeType` | Information about the company's type, e.g. `Bolag (On1)`. |
| `ledgerNoteHeader` | `String` | The header text displayed on a ledger note related to the organisation. |
| `ledgerNoteMessage` | `String` | The message text displayed on a ledger note related to the organisation. |
| `statusRemoved` | `Boolean` | Indicates if the company is marked as removed. |
| `statusDraft` | `Boolean` | Indicates if the company is in draft state. E.g. The company has been created but not activated. |
| `statusTemporary` | `Boolean` | Indicates if the company is in temporary state. E.g. The company has been created but not saved. |
| `statusFuture` | `Boolean` | Future (none draft) company. |
| `statusActive` | `Boolean` | True if today's date is between the company's start and end date. |
| `statusExpired` | `Boolean` | True if the company has expired. |
| `statusStart` | `Date` | The date when the company first become active. |
| `statusEnd` | `Date` | The date when the company will expire. |
| `actor` | `Actor` | Actor information about the company. |
| `properties(where: CompanyPropertyFilterInput, order: [CompanyPropertySortInput!])` | `[CompanyProperty!]` | All properties connected to the company. |
| `areas(where: AreaCompanyFilterInput, order: [AreaCompanySortInput!])` | `[AreaCompany!]` | All areas that are connected to the company. |
| `accountingTransactions(where: AccountingTransactionCompanyFilterInput, order: [AccountingTransactionCompanySortInput!])` | `[AccountingTransactionCompany!]` | All accounting transactions related to the company. |
| `entityTag` | `String!` | Opaque identifier assigned to a specific version of the resource.<br>The `entityTag` value changes when any property may have changed, excluding the properties `nodeType`, `accountingTransactions`, `areas`, and `properties`.<br>Subscriptions are notified when `entityTag` changes. |
| `appUrl` | `String` | Returns the url to the entity in the app. |

## Customer

Represents `customers` and `organisations` in the system. Customers are a specific type of actor that act as customers in the system.
Depending on the type, a customer can be either a person or an organisation.

Swe:
En kund är en form av aktör som används som en kund i systemet. Beroende på dess typ så kan en kund både vara en enskild person eller en orginsiation.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The customer/organisation's universally unique identifier. |
| `number` | `String` | A distinguished number, assigned by the system to each customer in the system. |
| `displayName` | `String` | The customers full name or organisation name, e.g. `Test Testsson` or `Mitt Bolag AB`. |
| `secondaryDisplayName` | `String` | The customers popular name. |
| `nodeClass` | `NodeClass` | The customer/organisation's node class, e.g. `Privatperson`, `Nyttjare` or `Föreningar och stiftelser`. Is used to indicate what type of customer it is. |
| `nodeType` | `NodeType` | Information about the customer's type, e.g. `Kund`. |
| `identityFullName` | `String` | The customer's full name or organisation name, e.g. `Test Testsson` or `Mitt Bolag AB`. |
| `identityName` | `String` | The customer's first name or organisation name, e.g. `Test` or `Mitt Bolag AB`. |
| `identityLastName` | `String` | The customer's last name. Used when the customer is not an organisation. |
| `identityOfficialNumberIssuer` | `String` | Issuer of official number if specified. E.g `se`. |
| `identityOfficialNumber` | `String` | Person or organisation registration number of the customer.<br><br>Swe:<br>Kundens person- eller orginsationsnummer. |
| `identityDateOfBirth` | `Date` | The customer's date of birth. Is not used if the customer is an organisation. |
| `vatRegistrationNumber` | `String` | The customer/organisation's vat registration number. |
| `localizationNumber` | `String` | The customer/organisation's localization number. |
| `statusCreated` | `DateTime` | The date and time when the customer/organisation first got created. |
| `statusChanged` | `DateTime` | The date and time when the customer/organisation last got changed. **Deprecated:** Use entityTag. |
| `statusRemoved` | `Boolean` | Indicates if the customer/organisation is marked as removed. |
| `statusType` | `String` | The actor status type, e.g  `Avliden` |
| `statusTemporary` | `Boolean` | Indicates if the customer/organisation is in temporary state. E.g. The customer/organisation has been created but not saved. Temporary customer/organisations is always removed. |
| `communications(where: ActorCommunicationFilterInput, order: [ActorCommunicationSortInput!])` | `[ActorCommunication!]` | All actor communication details. |
| `postalAddress` | `String` | The customer/organisation's postal address. |
| `postalAddress2` | `String` | The customer/organisation's second postal address. |
| `postalAddressPostCode` | `String` | The local postcode where the postal address is located. |
| `postalAddressCity` | `String` | The city where the postal address is located. |
| `postalAddressCountry` | `String` | The country in which the postal address is located. |
| `postalAddressCountryCode` | `String` | The code of the country in which the postal address is located. |
| `postalAddressCareOf` | `String` | The Care Of related to the postal address. |
| `postalAddressAttention` | `String` | The Attention related to the postal address. |
| `isOrganisation` | `Boolean` | True if the customer is an organisation. Both isOrganisation and isCustomer can be true. E.g `Enskild firma`. |
| `isCustomer` | `Boolean` | True if the customer is not an orgaisation. Both isCustomer and isOrganisation can be true. E.g `Enskild firma`. |
| `contracts(where: CustomerContractFilterInput, order: [CustomerContractSortInput!])` | `[CustomerContract!]` | All contracts that are or have been related to the customer. This includes removed, draft, expired, active, and future contracts.<br><br>Swe:<br>Alla avtalversioner kopplade till kunden, innehåller både utgångna, utkast, borttagna, aktiva och kommande avtalversioner. |
| `ledgerNotes(where: LedgerNoteFilterInput, order: [LedgerNoteSortInput!])` | `[LedgerNote!]` | All ledger notes that have been or are related to the customer.<br><br>Swe:<br>Alla avier som är eller har varit kopplade till kunden. |
| `invoices(where: InvoiceCustomerFilterInput, order: [InvoiceCustomerSortInput!])` | `[InvoiceCustomer!]` | All invoices that are and have been related to the customer.<br><br>Swe:<br>Alla fakturor som är eller har varit kopplade till kunden |
| `errandRows(where: ErrandRowActorFilterInput, order: [ErrandRowActorSortInput!])` | `[ErrandRowActor!]` | All errand rows that are or have been connected to the customer.<br><br>Swe:<br>Alla ärenderader som är eller har varit kopplade till kunden. |
| `errands(where: ErrandActorFilterInput, order: [ErrandActorSortInput!])` | `[ErrandActor!]` | All errands that are or have been connected to the customer.<br><br>Swe:<br>Alla ärenden som är eller har varit kopplade till kunden. |
| `economy` | `CustomerEconomy` | Displays the customer's summarized economy. E.g. The customer's total debt.<br>Cost: 250 |
| `loginId` | `String` | The customer's login id. |
| `entityTag` | `String!` | Opaque identifier assigned to a specific version of the resource.<br>The `entityTag` value changes when any property may have changed, excluding the properties `nodeType`, `nodeClass`, `contracts`, `ledgerNotes`, `invoices`, `errandRows` and `errands`.<br>Subscriptions are notified when `entityTag` changes. |
| `appUrl` | `String` | Returns the url to the entity in the app. |

## PricingMessage

A message attached to a pricing in the system.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The pricing message's universally unique identifier. |
| `statusRemoved` | `Boolean!` | Indicates if the message is marked as removed. |
| `statusStart` | `Date!` | The date when the message become active. |
| `statusEnd` | `Date!` | The date when the message will expire. |
| `transactionCreated` | `Transaction` | The transaction that created the message. |
| `aboutPricingPrice` | `PricingPrice` | The pricing price the massage is about |
| `text` | `String` | The message text. |

## VatType

A vat type.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `id` | `String!` | The vat type's universally unique identifier. |
| `displayName` | `String` | The vat type's display name. E.g. `Ingen Moms`, `Moms (25%)`. |
| `statusRemoved` | `DateTime` | When the vat type was deleted. |

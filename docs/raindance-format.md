# Raindance output and source mapping

This describes the layout emitted by `RaindanceWriter`, not a general Raindance format specification. Positions are **one-based and inclusive**. Every record is space-padded to its stated length; its following CRLF is not included in that length. All positions not listed below remain spaces. ISO-8859-1/Latin-1 encoding makes each supported character one byte.

`ConversionResult.ResultFile` returns these records as an already encoded `byte[]` for RAW file delivery, not a string. Within each `data.ledgerNoteAccountingsSync.nodes[]`, source rows are visited in their supplied ledger/row order. One emitted invoice has this structure:

```text
S customer                                      305 characters
H invoice header                                359 characters
R amount-bearing invoice text                   105 characters
R period-only invoice text, if a period exists   105 characters
K revenue accounting                            184 characters
K additional revenue accounting, if any          184 characters
R next amount-bearing invoice text
R its period-only text, if a period exists
K its revenue accounting
...
```

The second R is a text-only record: it has neither amount nor VAT code. The period is not appended to the priced R text or to the K comment. There is no extra row-type digit in the output; the amount/VAT fields distinguish an amount-bearing row from a text-only row.

## Source selection

In the tables, `node` is one source node, `row` is one entry in `node.ledgers[].rows[]`, and `distribution` is **the first** entry in `node.distributions`. Address fields come from its `postalAddress`, not `address`.

- Row description: first nonblank `row.ledgerRow.text.textDetailed`, otherwise `row.ledgerRow.text.text`.
- Rows whose selected description contains `Öresavrundning`, case-insensitively, are excluded completely.
- The invoice period is `node.ledgerNote.refersToPeriodDisplayName`. It is reused after every included invoice row in that node and in each associated K period field. It is not read from pricing messages, accounting dates, or ledger status dates.
- Revenue accounting records are those whose parsed account (`Konto`) starts with `3`. Other accounts are not exported as K records. Revenue records are grouped by the complete `accountDistributionCoding` string within each source row; zero-sum groups are omitted.
- A node with no included rows emits neither S nor H.

## S — customer, 305 characters

| Positions | Width | Field | Momentum source / rule |
| --- | ---: | --- | --- |
| 1 | 1 | Record type | `S` |
| 15–54 | 40 | Name | `node.ledgerNote.customer.displayName`; fallback to first nonblank line of `distribution.postalAddress.address`. |
| 55–94 | 40 | Care of | `distribution.postalAddress.careOf` |
| 95–134 | 40 | Street | Nonblank `streetAddress1` and `streetAddress2`, joined with one space. |
| 135–143 | 9 | Postcode | `distribution.postalAddress.postCode` |
| 145–174 | 30 | City | `distribution.postalAddress.city` |
| 175–190 | 16 | VAT registration number | For `FTG` only, `SE` + ten normalized identity digits + `01`; otherwise blank. |
| 195–206 | 12 | Identity | ASCII digits from `node.ledgerNote.customer.identityOfficialNumber`. |
| 210–211 | 2 | Country | `distribution.postalAddress.country.shortDisplayName` |
| 215–224 | 10 | Motpart | First nonblank parsed Motpart among included revenue records in the invoice. |
| 225–234 | 10 | Kundtyp | Mapped from `node.ledgerNote.customer.nodeClass.displayName`, below. |

Customer class mapping uses case-insensitive substring matches:

| Class text contains | Kundtyp |
| --- | --- |
| `Privatperson` | `PRIV` |
| `Näringsidkare` or `Föreningar` | `FTG` |
| `offentlig` | `ÖVR` |

Missing/unknown customer class or an identity with no ASCII digits fails conversion. Non-ASCII digits are rejected. The task strips non-digit separators from identity values but does not verify personal/organisation-number checksums. Customer numbers from Momentum are not written to a separate S customer-number field.

Blank name, address, or Motpart values are not rejected by the converter. Confirm that production data supplies the fields required by your receiving customer setup; identity validation alone does not establish that an S record can create a usable customer.

## H — invoice header, 359 characters

| Positions | Width | Field | Momentum source / rule |
| --- | ---: | --- | --- |
| 1 | 1 | Record type | `H` |
| 65–94 | 30 | Orderer reference | First nonblank `distribution.ordererReference1`, otherwise `ordererReference2`. |
| 145–156 | 12 | Customer identity | Same normalized customer identity as S positions 195–206. |
| 200–209 | 10 | Fakturanummer | **Always blank**, deliberately not copied from Momentum. |

All other H fields are blank. In particular, the queried `ledgerNote.number`, `ledgerNote.invoice.number`, `referenceNumber`, `due`, `toPay`, contract numbers, and company header/message fields are not currently written to H. Invoice numbering and other omitted defaults must be configured in the receiving Raindance import.

## R — invoice rows, 105 characters

Amount-bearing R:

| Positions | Width | Field | Momentum source / rule |
| --- | ---: | --- | --- |
| 1 | 1 | Record type | `R` |
| 3–56 | 54 | Radtext | Selected row description, truncated at 54 characters. No added period. |
| 57–62 | 6 | Reserved text space | Blank on amount-bearing rows. |
| 63–77 | 15 | Amount | Absolute `row.ledgerRow.netAmount` in minor units, right-aligned. |
| 78 | 1 | Credit marker | `-` when `netAmount < 0`; otherwise blank. |
| 79–81 | 3 | VAT code | `vatType.id == "standard"` → `K25`; null → `K00`. Other values fail. |

Text-only period R, immediately following the amount-bearing R:

| Positions | Width | Field | Momentum source / rule |
| --- | ---: | --- | --- |
| 1 | 1 | Record type | `R` |
| 3–62 | 60 | Radtext | `YYYY-MM` or `YYYY-MM - YYYY-MM` from the invoice period. |

Everything else in the text-only R is blank, including positions 63–81. If the source period is null, empty, or whitespace, this second R is omitted. A nonblank period must be a valid month or ascending month range; malformed values fail rather than silently disappearing. Literal parentheses are not written.

## K — accounting, 184 characters

| Positions | Width | Field | Source / rule |
| --- | ---: | --- | --- |
| 1 | 1 | Record type | `K` |
| 3–12 | 10 | Konto | Parsed coding |
| 13–22 | 10 | Ansvar | Parsed coding |
| 23–32 | 10 | Verksamhet | Parsed coding |
| 33–42 | 10 | Aktivitet | Parsed coding |
| 43–52 | 10 | Objekt | Parsed coding |
| 53–62 | 10 | Projekt | Parsed coding |
| 63–72 | 10 | Fri | Parsed coding |
| 73–82 | 10 | Motpart | Parsed coding |
| 125–139 | 15 | Amount | Absolute signed sum of the revenue group, in minor units, right-aligned. |
| 140 | 1 | Credit marker | `-` for a negative group sum; otherwise blank. |
| 145–174 | 30 | Comment | Selected source row description, truncated to 30 characters; no appended period. |
| 175–184 | 10 | Periodisering | Invoice period as `YYMM` or `YYMM YYMM`, left-aligned. |

For example, a source period `2026-07 - 2026-09` produces an R text `2026-07 - 2026-09` and K period `2607 2609`. The K field controls accounting periodisation; its presence does not itself print the rental period on the invoice.

### Accounting dimensions

`accountDistributionCoding` is split on spaces, ignoring empty parts. This is an integration-specific positional convention; it cannot represent an arbitrary missing dimension in the middle. One and two parts are treated as Konto and, optionally, Ansvar. With three or more parts, the last is Motpart:

| Part count | Ordered dimensions |
| ---: | --- |
| 1 | Konto |
| 2 | Konto, Ansvar |
| 3 | Konto, Ansvar, Motpart |
| 4 | Konto, Ansvar, Verksamhet, Motpart |
| 5 | Konto, Ansvar, Verksamhet, Aktivitet, Motpart |
| 6 | Konto, Ansvar, Verksamhet, Aktivitet, Objekt, Motpart |
| 7 | Konto, Ansvar, Verksamhet, Aktivitet, Objekt, Projekt, Motpart |
| 8 | Konto, Ansvar, Verksamhet, Aktivitet, Objekt, Projekt, Fri, Motpart |

More than eight parts fails conversion. Confirm this convention against the source's account coding setup before onboarding another company or dimension layout.

### Amounts and debit/credit

Amounts contain only magnitude digits. A minus sign belongs exclusively in the separate credit-marker field, never in the amount field. Amounts are multiplied by 100 and rounded to the nearest minor unit, with midpoint values rounded away from zero. The 15-digit amount field is enforced; values that cannot fit fail conversion.

Momentum's `records[].debit` describes the journal-side revenue posting. This invoice import uses the opposite transaction polarity:

| Source revenue record | Signed contribution to K group |
| --- | --- |
| `debit: false`, nonnegative `amount` | Positive: ordinary invoice / blank marker |
| `debit: true`, nonnegative `amount` | Negative: credit / `-` marker |

Every exported revenue record must contain an explicit debit flag and a nonnegative amount magnitude. The R sign comes independently from `ledgerRow.netAmount`. For each included source row, the signed sum of the exported K minor-unit amounts must equal the R minor-unit amount; a mismatch fails the entire conversion. This checks the exported revenue rows, not the complete source journal, VAT calculation, or invoice `toPay`.

## Text and validation boundaries

Leading/trailing field whitespace is trimmed. CR, LF, and tab inside a field become spaces; other control characters are rejected. Descriptive fields intentionally truncate to their mapped widths. Identity, postcode, country, dimension/code, period, and amount fields must fit without truncation. The final output is checked for Latin-1 representability; unsupported retained characters fail instead of becoming `?`.

Incoming `changeType` values, when present, must have ID `created` on candidate nodes, ledgers, and rows. The converter does not implement update/delete event semantics. Missing `changeType` is tolerated for existing saved JSON. Missing/null invoice and ledger/row structures are not treated as an empty successful invoice.

Fields fetched by the embedded query but not mapped above are not written. In particular, row pricing-message dates do not override the invoice-wide rental period. If different rows in one source node can refer to different periods, this mapping needs an explicit source-field change before that case is supported.

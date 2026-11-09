# Mutation and subscription inventory — not used by this module

Source: live Momentum schema introspection, 9 September 2026. Types, nullability, argument defaults, descriptions, and deprecation notices below are copied from that schema; descriptions can contain vendor typos. A declared field is not proof that every API account is authorized to read it. No customer or invoice payload values are included.

Notation: `T!` is non-null; `[T!]` permits a null list but not null items; `T` permits null. A blank/default-unspecified argument is not a documented server page-size default. For operational guidance, start with [the overview](README.md).

These **27 mutations** and **19 subscription fields** were discovered through read-only introspection only. No mutation or subscription was executed. They are outside the invoice export workflow, and this document does not authorize invoking them. Transport configuration, account permissions, side effects, retention, and delivery guarantees were not tested.

## Mutation

Create an ErrandRow for a given errand, using the errandId as input

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `createActorCommunication(input: CreateActorCommunicationInput!)` | `CreateActorCommunicationPayload!` | Creates an actor communication. |
| `updateActorCommunication(input: UpdateActorCommunicationInput!)` | `UpdateActorCommunicationPayload!` | Updates an actor communication.<br>Activates any updated removed row. |
| `removeActorCommunication(input: RemoveActorCommunicationInput!)` | `RemoveActorCommunicationPayload!` | Removes an actor communication. |
| `updateActorBookmark(input: UpdateActorBookmarkInput!)` | `UpdateActorBookmarkPayload!` | Updates an actor bookmark. |
| `removeActorBookmark(input: RemoveActorBookmarkInput!)` | `RemoveActorBookmarkPayload!` | Removes an actor bookmark. |
| `createEntityConversationReply(input: CreateEntityConversationReplyInput!)` | `CreateEntityConversationReplyPayload!` | This mutation creates a reply in a conversation. <br>If the entity connected to the provided entityId lacks an associated conversation, it creates one. <br>You can optionally provide conversation ID when the entity has no conversation. If no ID is provided for the conversation, the system auto-generates one.<br>Providing a conversation ID for an entity that already has a conversation triggers an error.<br>You can also optionally provide a reply ID. If no Id is provided, the system auto-generates one. |
| `updateEntityReply(input: UpdateEntityReplyInput!)` | `UpdateEntityReplyPayload!` | Updates an existing reply within a conversation. |
| `removeEntityReply(input: RemoveEntityReplyInput!)` | `RemoveEntityReplyPayload!` | Removes a entity reply from a conversation. |
| `restoreRemovedEntityReply(input: RestoreRemovedEntityReplyInput!)` | `RestoreRemovedEntityReplyPayload!` | Restores a removed entity reply.<br><br><br>**Returns:**<br>Reply |
| `markEntityReplyAsRead(input: MarkEntityReplyAsReadInput!)` | `MarkEntityReplyAsReadPayload!` | Marks a conversation reply as read for the specified actor.<br>Throws an error if the reply has already been marked as read, or if invalid identifiers are provided. |
| `createEntityReplyFiles(input: CreateEntityReplyFilesInput!)` | `CreateEntityReplyFilesPayload!` | Uploads one or multiple files to a existing reply<br><br><br>**Returns:**<br>ReplyFiles |
| `createEntityReplyFile(input: CreateEntityReplyFileInput!)` | `CreateEntityReplyFilePayload!` | Uploads one file to a existing reply<br><br><br>**Returns:**<br>ReplyFile |
| `removeFileFromEntityReply(input: RemoveFileFromEntityReplyInput!)` | `RemoveFileFromEntityReplyPayload!` | Removes a file from a existing reply<br><br><br>**Returns:**<br>ReplyFile |
| `createErrand(input: CreateErrandInput!)` | `CreateErrandPayload!` | Creates an Errand. <br>Errand will be in created as temporary, and will not be assigned an errand number until persisted by later mutation (e.g. using CreateErrandRow). |
| `updateErrandPets(input: UpdateErrandPetsInput!)` | `UpdateErrandPetsPayload!` | Update Errand pets information. <br>Text field for information, e.g. when visiting an apartment. |
| `createErrandRowMedia(input: CreateErrandRowMediaInput!)` | `CreateErrandRowMediaPayload!` | Upload a media file to an existing ErrandRow<br>File upload is handled using filestream.<br>This fileupload allows for files up to 6MB. |
| `createErrandRow(input: CreateErrandRowInput!)` | `CreateErrandRowPayload!` | Create an errand row for an errand.<br>Errand row will be sent to state "Hos handläggare" with the assigned actor of role "Handläggare" as active.<br>If related errand is in state temporary, errand will be persisted and assigned an errand number. |
| `updateErrandRowKeyManagement(input: UpdateErrandRowKeyManagementInput!)` | `UpdateErrandRowKeyManagementPayload!` | Update value of errand row key management. |
| `updateErrandRowErrorDescription(input: UpdateErrandRowErrorDescriptionInput!)` | `UpdateErrandRowErrorDescriptionPayload!` | Update errand row error description. |
| `updateMarketActorLatestActivity(input: UpdateMarketActorLatestActivityInput!)` | `UpdateMarketActorLatestActivityPayload!` | Updates a market actor latest activity date. |
| `markNotificationRead(input: MarkNotificationReadInput!)` | `MarkNotificationReadPayload!` | This mutation updates ActorMessage. |
| `createPrice(input: CreatePriceInput!)` | `CreatePricePayload!` | Creates a price. |
| `updatePrice(input: UpdatePriceInput!)` | `UpdatePricePayload!` | Updates a price. |
| `removePrice(input: RemovePriceInput!)` | `RemovePricePayload!` | Removes a price. |
| `createPricing(input: CreatePricingInput!)` | `CreatePricingPayload!` | Creates a pricing. |
| `updatePricing(input: UpdatePricingInput!)` | `UpdatePricingPayload!` | Updates a pricing.<br>Sets removed = false if row is removed. |
| `removePricing(input: RemovePricingInput!)` | `RemovePricingPayload!` | Removes a pricing. |

## Subscription

GraphQL subscriptions for real-time agent events.

| Field / arguments | Exact result type | Schema description |
| --- | --- | --- |
| `entityChange(input: SubscribeEntityChangeInput)` | `EntityChange` | Subscribe to all change events for all entities. |
| `equipment(input: SubscribeEquipmentInput)` | `EquipmentDTO` | Subscribe to equipment events. |
| `maintenance(input: SubscribeObjectInput)` | `ObjectDTO` | Subscribe to object events. |
| `actor(input: SubscribeActorInput)` | `ActorDTO` | Subscribe to actor events. |
| `area(input: SubscribeAreaInput)` | `AreaDTO` | Not described by the schema. |
| `buildingPart(input: SubscribeBuildingPartInput)` | `BuildingPartDTO` | Not described by the schema. |
| `constructionWork(input: SubscribeConstructionWorkInput)` | `ConstructionWorkDTO` | Not described by the schema. |
| `contract(input: SubscribeContractInput)` | `ContractDTO` | Subscribe to contract events. |
| `entityReply(input: EntityReplyInput)` | `EntityReplyDTO` | Subscribe to entity conversation reply events. |
| `errandRow(input: SubscribeErrandRowInput)` | `ErrandRowDTO` | Subscribe to errand row events. |
| `errand(input: SubscribeErrandInput)` | `ErrandDTO` | Subscribe to errand events. |
| `ledgerNote(input: SubscribeLedgerNoteInput)` | `LedgerNoteDTO` | Subscribe to ledger note events. |
| `ledger(input: SubscribeLedgerInput)` | `LedgerDTO` | Subscribe to ledger events. |
| `meterFacility(input: SubscribeMeterFacilityInput)` | `MeterFacilityDTO` | Not described by the schema. |
| `meter(input: SubscribeMeterInput)` | `MeterDTO` | Not described by the schema. |
| `pricing(input: SubscribePricingInput)` | `PricingDTO` | Subscribe to pricing events. |
| `property(input: SubscribePropertyInput)` | `PropertyDTO` | Not described by the schema. |
| `responsibilityEntityActor(input: SubscribeResponsibilityEntityActorInput)` | `ResponsibilityEntityActorDTO` | Not described by the schema. |
| `stairwell(input: SubscribeStairwellInput)` | `StairwellDTO` | Not described by the schema. |

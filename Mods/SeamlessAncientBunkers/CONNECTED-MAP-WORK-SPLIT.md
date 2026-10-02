# Parallel connected-map implementation

User authorized two chats on 2026-09-27. Both share the live directory; ownership below prevents conflicting edits. Refer to CONNECTED-MAP-GAPS.md for requirements and acceptance cases.

## This chat: transport and behavior (11 areas)

IDs 01, 03, 05, 06, 12, 16, 17, 18, 19, 20, 23: living-pawn carrying, slave eligibility, Biotech mechs, emergency travel, caravan/transporter gathering, social gatherings, animal transport/work, guests/allies, retreat and other combat travel, mental states, queued orders and alternative routes.

Owns Portal.cs, Graph.cs, Broker.cs, Manager.cs, Patches.cs, LinkedOrders.cs, Orders.cs, OrderFeedback.cs, Gatherings.cs, AnimalSupport.cs, EnemyPursuit.cs, GravshipSupport.cs, RobotSupport.cs and new transport/behavior modules. Owns new Tests/TransitProbe and BunkerConnectedTransit fixtures.

## Other chat: services and resources (12 areas)

IDs 02, 04, 07, 08, 09, 10, 11, 13, 14, 15, 21, 22: warden interactions, childcare/education interactions, maintenance, supply requests, corpse processing, bill counts/destinations, trade, other ordinary/DLC work, special needs, work priority fairness and fair candidate scans.

Owns WorkProbe.cs, Hauling.cs, LinkedResources.cs, ExtraNeeds.cs, NeedProbe.cs, NeedPatches.cs, LinkedPawnRoster.cs, BillSkillWarning.cs, ProcessorSupport.cs and new services/resources modules. Owns new Tests/ServicesProbe and BunkerConnectedServices fixtures. Extend material accessibility, ordinary tending supplies and availability warnings in this half. Coordinate before editing LinkedLogistics.cs, currently owned by the existing medical/burial chat.

## Shared rules

- Do not edit another owner's files; request a narrowly defined integration change through chat messages. The user authorized ongoing coordination between these two chats.
- Transport owns living-pawn movement; services owns discovering warden/childcare work. Services may request a public transport API rather than duplicating transfer code.
- Existing medical/burial and hunting/animal-combat work must be preserved. Wait for or coordinate integration before edits to their active files.
- Keep release metadata, shared csproj, README and historical TEST-REPORT unchanged during parallel work. Record results in separate CONNECTED-TRANSIT-REPORT.md / CONNECTED-SERVICES-REPORT.md.
- Compile with separate absolute output/intermediate directories inside BunkerConnectedTransit or BunkerConnectedServices. Do not concurrently overwrite the production assembly. This chat performs final integrated build after both halves are ready.
- Use separate disposable test saves/configuration/logs and distinct test flags. Never alter personal saves, active mod configuration, or stop the user's running game. Run helpers hidden. Do not publish to Steam unless separately authorized.
- Scope is implementation and meaningful engine validation, not just an updated plan. Report unresolved limitations precisely. Preserve physical map coordinates and native eligibility restrictions.
- Rules that change heat/gas/atmosphere or ranged combat through floors are design questions in the audit, not authorization to invent world physics. Maintain existing physical rules; fix concrete accounting/lifecycle errors found while validating.

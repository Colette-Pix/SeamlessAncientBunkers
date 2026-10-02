# Connected-map burial and surgery

Implemented and tested against RimWorld 1.6.4871 on 2026-09-27.

## Behavior

- Hauling work finds eligible graves across opened, enabled bunker links. The worker can start on the corpse map or travel to collect it first. The corpse is physically carried, and vanilla HaulToContainer performs the burial.
- Doctor work fetches missing medicine and surgery ingredients across the same links. Hauling work need not be enabled for the doctor. Ingredients are delivered near the patient, then the normal surgery job consumes them.
- Patients recognize reachable remote doctors when deciding whether to wait in bed. Patient work still needs to be enabled normally.
- Route safety, allowed areas, forbidden items, grave assignments/filters, reservations, medical-care categories, surgeon skill and suspended/incompletable bills remain effective. Recipe mixing rules and individually specified ingredients are respected.
- Source and recipient claims prevent competing transport plans. Collection and arrival revalidate the recipient. New intent values are appended to the existing enum, and the recipient reference is saved with the normal travel intent.

## Gameplay evidence

The isolated LogisticsProbe harness used a disposable opened-hatch fixture, Harmony, Core, the fixture's DLC, the production mod and the test mod. It never loaded or saved a personal colony.

- `Tests/Evidence/logistics-collect.log`: grave-filter and forbidden-corpse rejection; clean query restoration; automatic travel to the remote corpse and pickup; saved carried-corpse checkpoint.
- `Tests/Evidence/logistics-reload.log`: a fresh game process restored the carried corpse and intended grave, buried the bunker corpse on the surface, then buried a surface corpse in the bunker. Doctors physically brought medicine and wood to patients and completed native peg-leg operations in both directions with Hauling disabled. The tests also checked medical-care restrictions, remote doctor availability, and denial when the entrance was forbidden. Final marker: `[SAB LOGISTICS] ACCEPTANCE COMPLETE`.

The clean build had no compiler warnings/errors. The existing API/package audit passed all 71 checks. Gameplay assertions concern native operation completion, not guaranteed surgical success; ordinary surgery failure chances remain in effect. Individual modded recipes, multi-hop logistics and medical-supply save/reload were not separately simulated. The corpse transport save/reload exercises the shared saved-recipient and carried-cargo mechanism.

During fixture development, attempts to forbid a vanilla grave (which lacks a forbiddable component), construct a medical bill through the production-bill factory, and disable Patient work were corrected. Those failed fixture runs are retained only in the isolated BunkerLogistics folder, not as acceptance evidence.

Restart RimWorld to use the rebuilt installed assembly. No Workshop publication was performed.

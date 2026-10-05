# BusinessOS POS — Final UAT Checklist

This checklist is the release gate for Batch 14. Automated tests cover the core accounting, inventory,
cash, access-control, backup and UI invariants. Items marked **Pending reviewed translation CSV** must
remain open until the reviewed English/Dari/Pashto file is imported.

## Automated golden path

- [x] Fresh SQLite database initializes and seeds the Afghanistan POS profile.
- [x] Owner authentication and permission matrix work.
- [x] User creation, role assignment and password reset work.
- [x] Last active owner cannot be deactivated or lose the owner role.
- [x] System roles remain canonical/read-only; custom roles can receive selected permissions.
- [x] Supplier receipt posts inventory and payable at historical landed/FIFO cost.
- [x] Supplier payment reduces payable.
- [x] Cashier shift opens with opening float.
- [x] POS sale decrements physical stock and records COGS.
- [x] Sale return restores stock, reverses COGS and records refund evidence.
- [x] Customer collection reduces receivable.
- [x] Operating expense and other income feed reporting.
- [x] Shift expected cash reconciles to immutable cash movements.
- [x] Shift closes with exact actual cash.
- [x] Business day closes only after shift reconciliation.
- [x] Daily close snapshot reconciles sales, COGS, profit, purchases, collections, supplier payments,
      expenses and cash.
- [x] Dashboard and reports reconcile to the same business day.
- [x] Local backup is created, hashed and verified after the completed business day.
- [x] Desktop contains no USD/dollar currency labels; AFN is the product currency.
- [x] Every declared sidebar module maps to a concrete workspace.
- [x] Global DataGrid rows remain vertically centered with separators and theme-aware selected rows.

## Manual Windows UX

- [ ] Launch installed EXE from Start menu and desktop shortcut.
- [ ] First-run owner setup opens without clipping at 100%, 125% and 150% Windows scaling.
- [ ] Main window starts maximized and remains usable at the minimum supported window size.
- [ ] Classic theme: active sidebar item/icon is legible and selected table row is readable.
- [ ] Glass theme: cards, dialogs and selected rows remain legible against the glass background.
- [ ] POS keyboard flow: F2 search, F8 hold, Shift+F9 held sales, F9/Ctrl+Enter checkout.
- [ ] Receipt preview and print/reprint work with the intended thermal printer.
- [ ] Users & Roles: create staff user, assign role, reset password, create custom role, view audit.
- [ ] Products/Catalog: create/edit product, unit and barcode without horizontal clipping.
- [ ] Inventory: opening stock, adjustment, count, write-off and movement history scroll correctly.
- [ ] Purchasing: supplier, PO, receiving, payment and return screens fit without hidden controls.
- [ ] Customers: profile, receivable ledger and collection flow fit without hidden controls.
- [ ] Cash & Shifts: opening, movements, close/reopen and history are readable.
- [ ] Daily Closing: summary, revisions and business-day history are readable.
- [ ] Reports: filters, tables and CSV export work for a non-profit viewer and a profit viewer.
- [ ] Main Server mode starts and Client Terminal can pair using the one-time code.
- [ ] Revoked client terminal can no longer authenticate.
- [ ] Backup creation/verification/restore UI behaves as documented.
- [ ] Update check, download verification and updater handoff work with a valid signed release.

## Multilingual / RTL release gate

- [ ] **Pending reviewed translation CSV:** import approved Dari strings.
- [ ] **Pending reviewed translation CSV:** import approved Pashto strings.
- [x] Main signed-in shell already switches FlowDirection for Dari/Pashto.
- [x] First-run owner setup already switches FlowDirection immediately when Dari/Pashto is selected.
- [ ] **Pending reviewed translation CSV:** verify every navigation title/subtitle changes language.
- [ ] **Pending reviewed translation CSV:** verify every form label, button, table header and status message.
- [ ] **Pending reviewed translation CSV:** verify Dari/Pashto tables, tabs, dialogs and forms do not clip.
- [ ] **Pending reviewed translation CSV:** verify POS shortcuts still work while the UI is RTL.
- [ ] **Pending reviewed translation CSV:** verify receipt text and CSV headers use the selected locale where required.
- [ ] Switch English → Dari → Pashto → English in one session and confirm layout direction changes safely.

## Release decision

Batch 14 may be merged only after:
1. GitHub Windows CI passes restore, Release build, the complete unit/integration/UAT suite and vulnerable-package scan.
2. The reviewed translation CSV is imported.
3. Multilingual/RTL release-gate items above pass.
4. Installer smoke test passes on a clean Windows machine.

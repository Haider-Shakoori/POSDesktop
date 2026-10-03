# POSDesktop implementation plan

## Source-of-truth split
- **Haider-Shakoori/POS**: business behavior, calculations, workflows, permissions and reports.
- **Haider-Shakoori/PharmaDesktop**: WPF shell, themes, desktop UX, local/LAN patterns, updater, backup and packaging.
- **Haider-Shakoori/POSDesktop**: native Windows implementation.

## Locked product rules
- Currency: AFN only.
- Tax: disabled.
- Languages: English, Dari and Pashto.
- Offline-first local operation.
- LAN-ready multi-terminal deployment.
- Daily shift closing and business-day closing are first-class workflows.

## Delivery sequence
1. Desktop shell, Classic/Glass design system, module navigation and CI.
2. Authentication, local users, roles/permissions and session handling.
3. Local database foundation, seeding and settings.
4. Product catalog, units, categories, brands and barcodes.
5. Inventory, opening stock, stock movements, counts and write-offs.
6. Core POS sales screen, cart, keyboard shortcuts, receipt and hold/resume.
7. Returns, reversals, customer balances and collections.
8. Suppliers, purchase orders, goods receipts and supplier payments.
9. Cash drawer, shift opening/closing and operating expenses.
10. Daily/business-day closing and reconciliation.
11. Reports and dashboard live data.
12. LAN server/client mode and terminal management.
13. Backup/restore, update channel and installer/EXE packaging.
14. Multilingual RTL polish, UI regression and full golden-path UAT.

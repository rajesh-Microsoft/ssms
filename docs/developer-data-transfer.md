# Secure developer data transfer

This workflow copies registered society databases from one local SMMS Docker
environment to another. It is intended for authorized development machines only.

The database backups contain resident PII, financial records, UPI references,
uploaded-file paths, and password hashes. Encryption protects the data in transit
and at rest; it does not anonymize the database contents.

Prefer the fresh seeded databases created by `setup-local.ps1` whenever realistic
customer data is not required. Never commit a transfer artifact to Git, attach it
to a ticket, or send its passphrase in the same channel as the encrypted file.

## Security model

- Source databases are read using online `BACKUP DATABASE` operations only.
- Only databases registered in `SmmsControlDb.dbo.Societies` are exported.
- Every SQL backup uses `CHECKSUM` and is checked with `RESTORE VERIFYONLY`.
- The manifest records SHA-256 and row-count fingerprints for every database.
- The bundle is encrypted interactively with `age` passphrase encryption.
- The passphrase is never accepted as a script argument or written to a file.
- `SmmsControlDb` is not copied. The importer upserts only operational society
  metadata and preserves the target machine's local platform administrator.
- Platform users, platform audit logs, billing, support records, and onboarding
  contact fields are excluded from the control-plane transfer.
- Existing target tenant databases require an explicit `-ReplaceExisting` switch.
- Before replacement, the importer creates verified temporary SQL rollback backups
  inside the target SQL container, including `SmmsControlDb`.
- A failed import automatically restores replaced databases and control metadata,
  then removes databases that were newly created by that failed import.
- Plaintext staging files are removed after export/import, including failure paths.

Deleting files cannot guarantee physical erasure on SSD storage. Run this workflow
only on encrypted, access-controlled machines and delete encrypted artifacts when
the transfer and retention window are complete.

## Prerequisites

`setup-local.ps1` installs these prerequisites. Verify them on both Windows
machines before transferring data:

```powershell
age --version
docker info
```

The source and target must run the same SMMS commit. The importer refuses a
mismatch unless the operator explicitly supplies `-AllowCommitMismatch`.

## Export from the source machine

Open PowerShell in the repository root. Export all registered societies:

```powershell
.\tools\export-dev-data.ps1 `
  -OutputDirectory C:\SecureTransfer `
  -AcknowledgePii
```

Export selected societies:

```powershell
.\tools\export-dev-data.ps1 `
  -OutputDirectory C:\SecureTransfer `
  -SocietyKeys aadya,demo `
  -AcknowledgePii
```

Uploads are not part of this workflow. They live in one shared volume, cannot be
safely selected by society, and cannot be transactionally restored with a tenant
database. Restored attachment rows can therefore point to files absent on the
target. Design a separate tenant-isolated file migration before transferring
uploaded evidence.

Enter a strong, unique passphrase directly into the `age` prompt. Do not leave the
prompt empty: `age` will otherwise generate and display a passphrase in the terminal.
The output is:

- `smms-dev-data-<timestamp>.tar.age`
- `smms-dev-data-<timestamp>.tar.age.sha256`

Transfer both files through an approved encrypted file-transfer channel. Send the
passphrase separately, for example through an approved password manager.

## Import on the target machine

Copy the `.age` and `.sha256` files to a folder outside the Git checkout. From the
repository root, import into an empty target:

```powershell
.\tools\import-dev-data.ps1 `
  -BundlePath C:\SecureTransfer\smms-dev-data-<timestamp>.tar.age `
  -AcknowledgePii
```

A machine initialized by `setup-local.ps1` already has fresh Aadya and Aaradya
databases. Replacing them therefore requires explicit consent:

```powershell
.\tools\import-dev-data.ps1 `
  -BundlePath C:\SecureTransfer\smms-dev-data-<timestamp>.tar.age `
  -ReplaceExisting `
  -AcknowledgePii
```

Temporary rollback backups stay inside the SQL container while validation runs.
They are deleted after success. If both import and rollback fail, the script keeps
those files and reports their exact container path for manual recovery.

Only use `-AllowCommitMismatch` after reviewing migration compatibility. The API
may apply newer migrations when it starts, so row-count validation alone cannot
make an arbitrary cross-version restore safe.

## Validation performed automatically

The importer refuses completion unless all checks pass:

1. Outer encrypted-file SHA-256, when the sidecar is present.
2. Authenticated `age` decryption.
3. Manifest schema and PII classification.
4. SHA-256 of every `.bak` file.
5. SQL Server `RESTORE VERIFYONLY ... WITH CHECKSUM`.
6. Successful restore with each database in `ONLINE` state.
7. Users, Members, Collections, and Expenses row counts matching the source.
8. Operational society records upserted and read back from the local control plane.
9. Source and target Git commits matching unless explicitly overridden.
10. API health returning `Healthy` with the control database connected.

After validation, test both login surfaces:

- `http://admin.localtest.me:8080` uses the VM-local platform account.
- `http://<society>.localtest.me:8080` uses users restored with that society DB.

Change imported user passwords before giving the environment to another person.
Disable payment gateway credentials and allowlists in `.env`; the transfer scripts
do not copy `.env`, Razorpay keys, PhonePe keys, or other application secrets.

## Cleanup

After acceptance:

```powershell
Remove-Item C:\SecureTransfer\smms-dev-data-*.age*
```

Keep the source's normal operational backups. This workflow never deletes or
modifies source database rows or source uploads.

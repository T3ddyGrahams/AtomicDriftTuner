# Publish Preview

In GitHub, open **Actions → Publish Preview → Run workflow** on **main**.
Enter a new version such as `0.9.0-preview.9` and select a link lifetime.

The workflow reuses the existing ADT build and regression/layout checks, uploads
the installer and portable ZIP directly to private R2, verifies their sizes and
SHA-256 metadata, then asks the Worker to prepare an announcement. Open the
announcement from the workflow summary and sign in to the manager as usual.
Test both download links and copy the post into your supporter channel.

The workflow does not send Discord messages or publish a GitHub Release.
Preview packages and signed URLs are not uploaded as GitHub Actions artifacts
or printed in its summary. Existing standalone SignPath builds are unchanged.
Packages are currently unsigned, matching the existing build pipeline.

## One-time configuration

Keep the existing `DOWNLOAD_TOKEN` secret in Cloudflare. Do not copy it to
GitHub, change it, or use it for this setup.

1. In Cloudflare R2, create S3 credentials with **Object Read & Write**, scoped
   to the existing private preview bucket only. Keep the bucket private.
2. In GitHub repository **Settings → Secrets and variables → Actions**, add:

   | Kind | Name | Value |
   | --- | --- | --- |
   | Secret | `R2_ACCESS_KEY_ID` | New bucket-scoped S3 access key ID |
   | Secret | `R2_SECRET_ACCESS_KEY` | Matching S3 secret access key |
   | Variable | `R2_ACCOUNT_ID` | Cloudflare account ID, 32 hexadecimal characters |
   | Variable | `R2_BUCKET_NAME` | Existing private preview bucket name |
   | Secret | `PREVIEW_PUBLISH_TOKEN` | A new random credential, at least 32 random bytes |

3. Add that same new `PREVIEW_PUBLISH_TOKEN` as a **secret** in the existing
   Cloudflare Worker's runtime settings. It only authorizes publishing preflight
   and preparing announcements for a specific preview version. The API returns
   a manager sign-in URL, never signed downloads or an admin session.
4. Restrict changes to workflow files/main to trusted maintainers. These
   publishing credentials grant access to private preview files.

Enter secrets directly in the service settings; do not paste them into chat,
source code, issue comments, or workflow inputs. No Wrangler configuration change
is required. The automation uses the branded Worker address:
`https://adt-preview-downloads.atomicdrifttuner.workers.dev`.

## Failures and reruns

Missing configuration fails before the expensive build. Existing package names
are rejected before build and checked again before upload, so choose a new
preview version for a new build. Concurrent Publish Preview runs are serialized.
Do not upload the same version manually while a run is publishing.

If only one upload succeeds, no announcement is prepared. Inspect the private
bucket and publish a new version; partial files are not deleted automatically.
If both uploads succeeded but announcement preparation failed, open the manager
and generate the post for that version without rebuilding or overwriting it.
Expired prepared announcements return an expiry notice; generate fresh links
through the manager. The private `.adt-announcements/` records contain signed
links and are excluded from build discovery because they are JSON files.

## Validation

Run the Worker and automation tests without production credentials:

```powershell
node --experimental-default-type=module --test cloudflare/preview-downloads/*.test.mjs
```

The first real Publish Preview run additionally verifies GitHub-hosted build
tools, actual R2 permissions, uploads, and Worker runtime configuration.

References: [R2 S3 credentials](https://developers.cloudflare.com/r2/get-started/s3/),
[reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).

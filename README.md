# Git Repository Uploader

A Windows desktop app for uploading a folder to **GitHub, GitLab, or Gitea**, creating commits, and publishing updates through a simple interface.

Choose an upload type, fill in the fields, and start the upload. The app runs the Git commands and displays the results in its activity log.

![First upload to Gitea](assets/first-upload.png)

## Features

- **First upload:** initialize the repository, configure the commit author, connect to the service, commit, and push.
- **Subsequent uploads:** select a folder and enter a commit message; the app uses the existing remote connection.
- **GitHub, GitLab.com, and Gitea**, including GitLab subgroups and Gitea instances on custom HTTPS domains and ports.
- **Automatic rebase** when remote changes need to be integrated.
- **English and Italian**, with the selected language remembered after closing the app.
- **Dark interface**, with readable input fields, dropdown menus, and an activity log.
- **Browse menu** with the original Windows folder dialog and quick access to your last three successfully uploaded projects.
- Respects **`.gitignore`**, includes file deletions, and preserves local commits when an upload fails.

## Requirements

- Windows 10 or Windows 11 with .NET Framework 4.8 or later.
- [Git for Windows](https://git-scm.com/download/win), with Git Credential Manager enabled for account sign-in.
- An account on the selected service and an existing repository with write permission.
- An internet connection for synchronization and uploads.

The executable requires no app installation, Python, or Node.js.

## Quick start

1. Double-click **`Git Repository Uploader.exe`**.
2. Choose `English` or `Italiano` from **Language**. English is the default on first launch.
3. Choose `First upload` or `Subsequent uploads` from **Upload type**.
4. Fill in the fields and click the upload button.

In the Italian interface, the menus are labeled **Lingua**, **Tipo di caricamento**, and **Piattaforma**.

### First upload

Create the repository on your chosen service first. An empty repository is the simplest starting point. If the remote branch already exists, the app attempts to integrate its history with a rebase.

| Field | What to enter |
| --- | --- |
| Project folder | The project's root folder, selectable with `Browse…`. |
| Platform | `GitHub`, `GitLab`, or `Gitea`. |
| Account email | The email address to associate with your commits. |
| Username / name | Your username or the commit author's name. |
| Repository link | The repository's HTTPS URL on the selected service. |
| Commit message | A description of the upload; the initial value is `Initial upload`. |

Example repository links:

```text
GitHub: https://github.com/username/repository
GitLab: https://gitlab.com/username/project
GitLab with a subgroup: https://gitlab.com/group/subgroup/project
Gitea: https://gitea.com/username/repository
Gitea on your own server: https://git.example.com:3443/team/repository.git
Gitea with a URL prefix: https://git.example.com/gitea/team/repository.git
```

The `.git` suffix is optional for standard repository links. For Gitea installed under a URL prefix, copy the full HTTPS clone URL ending in `.git` from the repository's **Code** menu. Use the repository URL without page paths such as `/tree/main`, `/-/tree/main`, or `/src/branch/main`.

Click **Upload to GitHub**, **Upload to GitLab**, or **Upload to Gitea**. The app creates a `main` branch for a new repository and keeps the current branch for an existing one. Name and email are configured only for the selected folder.

### Subsequent uploads

![Subsequent uploads](assets/updates.png)

1. Edit your project files.
2. Select **Subsequent uploads**.
3. Select the same root folder and enter a message, such as `Fix login form`.
4. Click **Upload changes**.

You do not need to enter the platform, email, or repository URL again: the app uses the branch's configured remote. If there are no new changes, it skips creating an empty commit and still attempts to synchronize and upload any unpublished local commits.

## Account sign-in

The name and email fields identify the commit author. They do not sign you in to your account.

Authentication is handled by Git Credential Manager. Complete the account sign-in window when prompted. For GitHub and GitLab.com, this normally opens your browser. For GitLab.com, the app selects browser authentication, supported by the [credential manager](https://github.com/git-ecosystem/git-credential-manager/blob/main/docs/gitlab.md).

For Gitea, enter your account username in the credential manager prompt and use an access token as the password. Generate the token in your Gitea account's **Settings → Applications**, with permission to write to the repository. Gitea requires a token for Git over HTTPS when two-factor authentication is enabled; see the [Gitea authentication documentation](https://docs.gitea.com/usage/user-setting/multi-factor-authentication/). Your instance may also allow an account password when two-factor authentication is disabled.

Passwords and tokens are not stored in the app's preferences and should not be included in the repository URL. Saved credentials are managed by your configured Git credential helper. A self-hosted Gitea server must use HTTPS with a certificate trusted by Git; custom HTTPS ports and URL prefixes are supported. The app does not disable certificate verification or configure the server.

The language preference is stored at:

```text
%LOCALAPPDATA%\GithubSetup\language.txt
```

### Recent uploads

Click **Browse…** to choose between:

- **Open folder…**: opens the standard Windows Explorer folder selection dialog, with an address bar and a **Search** box to find folders quickly. Search within the current location, select the matching folder, and confirm with **Select folder**.
- **Recents**: shows your last **three distinct folders with successful uploads**, newest first. Click an entry to select that folder immediately.

In Italian, the choices are **Apri cartella…** and **Recenti**. Cancelling the Windows dialog leaves your current folder unchanged.

![Browse menu with Open folder and Recents](assets/recent-folders.png)

The list is remembered after closing the app. Uploading the same folder again moves it to the top. Failed uploads do not add folders to the list, and folders that no longer exist are hidden. Hover over a recent entry to see its full path.

Recent paths are stored only on your computer at `%LOCALAPPDATA%\GithubSetup\recent-folders.txt`. This history is not included in the app's distribution or GitHub upload package.

## Rebase and conflicts

Before pushing, the app fetches changes from the remote branch. If those changes are not already in the local history, it runs a rebase.

- **Successful rebase:** the upload proceeds.
- **Conflicts:** the app aborts the rebase, preserves the local commit, and lists the files that need manual reconciliation. The upload stops.
- **Failed push:** the commit remains saved locally, and you can retry the upload.

The app does not automatically choose which version to keep when changes conflict. After the rebase is aborted, the folder returns to its previous state and may not contain conflict markers.

## Troubleshooting

| Issue | What to do |
| --- | --- |
| Git is not found | Install Git for Windows and reopen the app. |
| The folder is not connected to a remote | Use `First upload` first. |
| A subfolder was selected | Select the root folder shown in the message. |
| Access is denied | Check your account and repository permissions, then complete sign-in. For Gitea, check that the access token can write to the repository. |
| The URL is invalid | Use the repository's HTTPS URL. GitHub and GitLab require their public domains; Gitea accepts your instance's domain. With a Gitea URL prefix, use the clone URL ending in `.git`. |
| Gitea reports a certificate error | Ask the server administrator to provide a valid HTTPS certificate and configure Git to trust the issuing certificate authority if needed. |
| Rebase stopped because of conflicts | Read the file names in the log and reconcile the changes before retrying. |
| The service rejected the push | Read the Git message: the branch may be protected, your account may lack permission, or additional remote changes may have arrived. |

## Source code and building

The app is written in **C# with Windows Forms** and uses Git for Windows for repository operations.

```text
Git Repository Uploader/
├── Git Repository Uploader.exe
├── README.md
├── build.ps1
├── .gitignore
├── assets/
├── src/
│   ├── GitUploader.cs
│   ├── UnifiedWindow.cs
│   ├── WindowsFolderPicker.cs
│   └── app.manifest
└── tests/
    └── SmokeTests.cs
```

To rebuild the app, open PowerShell in the project folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

To build the app and run the checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -RunTests
```

The checks use local test repositories without publishing files to hosted services. They cover first uploads, updates, deletions, `.gitignore`, rebase, commit preservation after conflicts, URL validation for all three platforms, the Gitea menu in both languages, saved language preferences, and recent-folder persistence and selection. Live authentication and server-specific policies require an account on the chosen service. Test folders are created under `work/`, which is excluded from version control.

## Scope of this version

The first-upload menu supports HTTPS repositories on **github.com**, **gitlab.com**, and **Gitea instances on public or private hosts**. HTTP and SSH links are not accepted for first uploads. Subsequent uploads use the folder's existing Git remote. The app does not create remote repositories, manage merge requests or pull requests, or resolve conflicts for you. Service rules, including branch protection, still apply.

# SonNuage Android

[English](README.md) · **Français**

Application Android de [SonNuage](https://github.com/SonNuage/sonnuage), serveur de musique auto-hébergé :
.NET MAUI Blazor Hybrid (mêmes pages que le client web) et service de lecture Media3 (notification, Android Auto).

État : fonctionne, utilisée tous les jours. Le projet est jeune et encore en développement.

| Dossier | Rôle |
|---|---|
| `src/SonNuage.Mobile` | Application : hôte Blazor, lecteur Media3, Android Auto, stockage sécurisé des comptes |
| `build/` | Image de build (SDK Android + workload MAUI) et script d'APK sans Android Studio |

Les pages et composants viennent de la bibliothèque `SonNuage.UI` du dépôt
[sonnuage](https://github.com/SonNuage/sonnuage) :

- **en CI** : paquet NuGet publié par la CI de `sonnuage` (version dans `Directory.Packages.props`)
- **en développement** : si `sonnuage` est cloné à côté de ce dépôt (`../sonnuage`), ses sources sont utilisées
  directement (voir `Directory.Build.props`, propriété `SonNuageRepo`).

Pour suivre une nouvelle version du serveur, monter `SonNuage.UI` dans `Directory.Packages.props` à la version
publiée (`X.Y.Z` pour un tag du serveur, `0.1.0-ci.N` pour un build de `main`).

## APK sans Android Studio

```bash
docker build -f build/android-sdk.Dockerfile -t sonnuage-android-sdk build
```

```bash
docker run --rm -v "$PWD:/host:ro" -v "$PWD/../sonnuage:/sonnuage-src:ro" -v "$PWD/out:/out" -v sonnuage-nuget:/root/.nuget/packages sonnuage-android-sdk sh /host/build/build-apk.sh Release
```

Un seul fichier est produit : `out/sonnuage-release.apk`, signé et installable. `dotnet publish` génère aussi un
`org.pmjb.sonnuage.apk` **non signé** : c'est un intermédiaire, Android refuse de l'installer, il n'est donc pas
copié. Sans keystore, l'APK local est signé avec la clé de debug de l'image : pour mettre à jour par-dessus un APK
de la CI (autre clé), il faut d'abord désinstaller l'appli.

## Fonctionnement

- **Comptes** : au premier lancement, l'application demande l'adresse du serveur puis l'identifiant. Plusieurs
  comptes (un utilisateur sur un serveur, éventuellement plusieurs serveurs) peuvent être connectés en même temps.
  **Réglages › Comptes** affiche le compte actif, permet d'en ajouter un, de changer de compte actif ou de se
  déconnecter d'un compte précis. Le compte actif fixe la bibliothèque, la lecture, Android Auto et la notification.
  En changer arrête la lecture et recharge la file sauvegardée de l'autre compte.
- Chaque compte a ses propres jetons (SecureStorage, renouvelés un par un avec un verrou par compte), sa file
  d'attente, ses réglages de lecture et ses entrées du cache audio. Une installation mono-compte antérieure est
  migrée automatiquement en un premier compte (session, file et réglages compris).
- **Plantages** : toute exception non gérée (.NET, tâches, Java) est écrite dans `crash.log` (dossier privé de
  l'appli) et dans logcat (`adb logcat -s SonNuage`). Au lancement suivant, un écran natif affiche le rapport avec un
  bouton « Copier le rapport ». Si le démarrage lui-même échoue, cet écran remplace l'interface.

## CI/CD GitLab

[.gitlab-ci.yml](.gitlab-ci.yml) : image de build (quand sa recette change) → APK signé en artefact
(`dist/sonnuage-X.Y.Z.apk`, signature vérifiée par `apksigner`) → sur `main`, semantic-release crée le tag `vX.Y.Z`
et sa release, à laquelle le pipeline du tag joint l'APK (registre de paquets génériques).

La signature utilise les variables masquées `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS` et `ANDROID_KEY_PASSWORD`. Elles portent la clé qui signait déjà les APK : une autre clé
obligerait à désinstaller l'application avant de mettre à jour. Un nouveau keystore se crée avec :

```bash
keytool -genkeypair -v -keystore sonnuage.keystore -alias sonnuage -keyalg RSA -keysize 4096 -validity 10000
```

### Versions

Les versions sont publiées par [semantic-release](https://semantic-release.gitbook.io/) ([.releaserc.json](.releaserc.json)),
d'après les messages de commit au format [Conventional Commits](https://www.conventionalcommits.org/fr/) :
`fix: …` donne un correctif (1.0.1), `feat: …` une version mineure (1.1.0), `feat!: …` ou un pied de page
`BREAKING CHANGE:` une version majeure (2.0.0). `docs`, `chore`, `ci`, `refactor`, `test` ne publient rien.
Sur `main`, le job `semantic-release` crée le tag `vX.Y.Z` et la release GitLab avec ses notes. Le pipeline du
tag publie ensuite la version. Il utilise `GITLAB_TOKEN`, jeton du groupe `sonnuage` (variable masquée et protégée,
tags `v*` protégés).

### GitHub Actions

[.github/workflows/ci.yml](.github/workflows/ci.yml) produit le même APK signé sur GitHub. Il extrait le dépôt
`sonnuage` du même propriétaire à côté de celui-ci et compile contre ses sources, sans registre NuGet. Secrets à
définir : les quatre variables de signature ci-dessus, plus `SONNUAGE_REPO_TOKEN` (jeton en lecture) si le dépôt
`sonnuage` est privé. Sur un tag `vX.Y.Z`, l'APK est joint à une release GitHub.

## Licence

SonNuage est un logiciel libre sous licence [GNU AGPL v3.0 ou ultérieure](LICENSE) : chacun peut l'utiliser,
l'étudier, le modifier et le redistribuer. Une version modifiée, y compris proposée comme service en ligne, doit
publier son code source sous la même licence.

Copyright © 2026 pmj.bailly et les contributeurs de SonNuage.

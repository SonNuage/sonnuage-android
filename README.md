# Localify Android

Application Android de [Localify](https://git.pmjb.org/pmj.bailly/localify), serveur de musique auto-hébergé :
.NET MAUI Blazor Hybrid (mêmes pages que le client web) et service de lecture Media3 (notification, Android Auto).

| Dossier | Rôle |
|---|---|
| `src/Localify.Mobile` | Application : hôte Blazor, lecteur Media3, Android Auto, stockage sécurisé des comptes |
| `build/` | Image de build (SDK Android + workload MAUI) et script d'APK sans Android Studio |

Les pages et composants viennent de la bibliothèque `Localify.UI` du dépôt
[localify](https://git.pmjb.org/pmj.bailly/localify) :

- **en CI** : paquet NuGet publié par la CI de `localify` (version dans `Directory.Packages.props`) ;
- **en développement** : si `localify` est cloné à côté de ce dépôt (`../localify`), ses sources sont utilisées
  directement (voir `Directory.Build.props`, propriété `LocalifyRepo`).

Pour suivre une nouvelle version du serveur, monter `Localify.UI` dans `Directory.Packages.props` à la version
publiée (`X.Y.Z` pour un tag du serveur, `0.1.0-ci.N` pour un build de `main`).

## APK sans Android Studio

```bash
docker build -f build/android-sdk.Dockerfile -t localify-android-sdk build
```

```bash
docker run --rm -v "$PWD:/host:ro" -v "$PWD/../localify:/localify-src:ro" -v "$PWD/out:/out" -v localify-nuget:/root/.nuget/packages localify-android-sdk sh /host/build/build-apk.sh Release
```

Un seul fichier est produit : `out/localify-release.apk`, signé et installable. `dotnet publish` génère aussi un
`org.pmjb.localify.apk` **non signé** : c'est un intermédiaire, Android refuse de l'installer, il n'est donc pas
copié. Sans keystore, l'APK local est signé avec la clé de debug de l'image : pour mettre à jour par-dessus un APK
de la CI (autre clé), il faut d'abord désinstaller l'appli.

## Fonctionnement

- **Comptes** : au premier lancement, l'application demande l'adresse du serveur puis l'identifiant. Plusieurs
  comptes (un utilisateur sur un serveur, éventuellement plusieurs serveurs) peuvent être connectés en même temps ;
  **Réglages › Comptes** affiche le compte actif, permet d'en ajouter un, de changer de compte actif ou de se
  déconnecter d'un compte précis. Le compte actif fixe la bibliothèque, la lecture, Android Auto et la notification ;
  en changer arrête la lecture et recharge la file sauvegardée de l'autre compte.
- Chaque compte a ses propres jetons (SecureStorage, renouvelés un par un avec un verrou par compte), sa file
  d'attente, ses réglages de lecture et ses entrées du cache audio. Une installation mono-compte antérieure est
  migrée automatiquement en un premier compte (session, file et réglages compris).
- **Plantages** : toute exception non gérée (.NET, tâches, Java) est écrite dans `crash.log` (dossier privé de
  l'appli) et dans logcat (`adb logcat -s Localify`). Au lancement suivant, un écran natif affiche le rapport avec un
  bouton « Copier le rapport » ; si le démarrage lui-même échoue, cet écran remplace l'interface.

## CI/CD GitLab

[.gitlab-ci.yml](.gitlab-ci.yml) : image de build (quand sa recette change) → APK signé en artefact
(`dist/localify-X.Y.Z.apk`, signature vérifiée par `apksigner`) → sur tag `vX.Y.Z`, APK dans le registre de
paquets et release GitLab.

La signature utilise les variables masquées `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS` et `ANDROID_KEY_PASSWORD`. Elles portent la clé qui signait déjà les APK : une autre clé
obligerait à désinstaller l'application avant de mettre à jour. Un nouveau keystore se crée avec :

```bash
keytool -genkeypair -v -keystore localify.keystore -alias localify -keyalg RSA -keysize 4096 -validity 10000
```

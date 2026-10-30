#!/bin/sh
# Build de l'APK dans l'image sonnuage-android-sdk sans toucher aux bin/obj de l'hôte :
#   docker run --rm -v "$PWD:/host:ro" -v "$PWD/../sonnuage:/sonnuage-src:ro" -v "$PWD/out:/out" \
#     -v sonnuage-nuget:/root/.nuget/packages sonnuage-android-sdk sh /host/build/build-apk.sh [Debug|Release]
# Le dépôt sonnuage monté en /sonnuage-src fournit SonNuage.UI en sources ; sans lui, le paquet NuGet est utilisé
# (registre privé : définir SONNUAGE_NUGET_TOKEN, un jeton d'accès GitLab avec read_api).
set -e
CONFIG=${1:-Release}
rm -rf /work && mkdir -p /work
mkdir -p /work/app
cp -r /host/src /host/Directory.Build.props /host/Directory.Packages.props /host/global.json /host/nuget.config /work/app/
if [ -d /sonnuage-src ]; then
  mkdir -p /work/sonnuage && cp -r /sonnuage-src/src /sonnuage-src/Directory.Build.props /sonnuage-src/Directory.Packages.props /sonnuage-src/global.json /work/sonnuage/
fi
find /work -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
cd /work/app
if [ -n "$SONNUAGE_NUGET_TOKEN" ]; then
  dotnet nuget update source sonnuage --username token --password "$SONNUAGE_NUGET_TOKEN" --store-password-in-clear-text --configfile nuget.config
fi
dotnet publish src/SonNuage.Mobile/SonNuage.Mobile.csproj -f net10.0-android -c "$CONFIG" -o /work/publish $EXTRA_ARGS
# Seul l'APK signé est installable : l'autre (org.pmjb.sonnuage.apk) n'est qu'un intermédiaire non signé.
cp /work/publish/*-Signed.apk "/out/sonnuage-$(echo "$CONFIG" | tr '[:upper:]' '[:lower:]').apk"
ls -l /out/*.apk

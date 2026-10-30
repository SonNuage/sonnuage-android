#!/bin/sh
# Build de l'APK dans l'image localify-android-sdk sans toucher aux bin/obj de l'hôte :
#   docker run --rm -v "$PWD:/host:ro" -v "$PWD/../localify:/localify-src:ro" -v "$PWD/out:/out" \
#     -v localify-nuget:/root/.nuget/packages localify-android-sdk sh /host/build/build-apk.sh [Debug|Release]
# Le dépôt localify monté en /localify-src fournit Localify.UI en sources ; sans lui, le paquet NuGet est utilisé
# (registre privé : définir LOCALIFY_NUGET_TOKEN, un jeton d'accès GitLab avec read_api).
set -e
CONFIG=${1:-Release}
rm -rf /work && mkdir -p /work
mkdir -p /work/app
cp -r /host/src /host/Directory.Build.props /host/Directory.Packages.props /host/global.json /host/nuget.config /work/app/
if [ -d /localify-src ]; then
  mkdir -p /work/localify && cp -r /localify-src/src /localify-src/Directory.Build.props /localify-src/Directory.Packages.props /localify-src/global.json /work/localify/
fi
find /work -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
cd /work/app
if [ -n "$LOCALIFY_NUGET_TOKEN" ]; then
  dotnet nuget update source localify --username token --password "$LOCALIFY_NUGET_TOKEN" --store-password-in-clear-text --configfile nuget.config
fi
dotnet publish src/Localify.Mobile/Localify.Mobile.csproj -f net10.0-android -c "$CONFIG" -o /work/publish $EXTRA_ARGS
# Seul l'APK signé est installable : l'autre (org.pmjb.localify.apk) n'est qu'un intermédiaire non signé.
cp /work/publish/*-Signed.apk "/out/localify-$(echo "$CONFIG" | tr '[:upper:]' '[:lower:]').apk"
ls -l /out/*.apk

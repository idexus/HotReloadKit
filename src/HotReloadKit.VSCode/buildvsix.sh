rm service.zip
cd ..
rm -r ./HotReloadKit.VSCodeService/bin
rm -r ./HotReloadKit.VSCodeService/obj
rm -r ./HotReloadKit.VSCode/out
rm -r ./HotReloadKit.VSCode/service
zip -r service.zip HotReloadKit.VSCodeService Shared
mv service.zip ./HotReloadKit.VSCode
cd HotReloadKit.VSCode
vsce package
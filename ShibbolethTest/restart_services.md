**IIS
```powershell
Restart-Service shibd_Default
iisreset
```

**Tomcat
```powershell
Restart-Service Tomcat10
Start-Sleep -Seconds 20
# ステータス（テキストが返る）
Invoke-WebRequest http://localhost:8080/idp/profile/status -UseBasicParsing | Select-Object -ExpandProperty Content
# メタデータ（<EntityDescriptor ...> が返る）
(Invoke-WebRequest http://localhost:8080/idp/shibboleth -UseBasicParsing).Content.Substring(0,300)
```

<%@ Language="VBScript" %>
<html><body>
<h2>Authentication Test</h2>
REMOTE_USER = [<%= Request.ServerVariables("REMOTE_USER") %>]<br>
AUTH_TYPE   = [<%= Request.ServerVariables("AUTH_TYPE") %>]
</body></html>

# SSLProxyServer
ENGISH
Instructions to the proxy server

Proxy server works via HTTP and HTTPS protocol. To process requests over HTTPS, the SSL certificate installed in Trusted Root Certification Authorities requires. 
The certificate must be named in the same way as the domain to which the request is directed.
Below is a tutorial to create a certificate to habr.com domain (run commands in PowerShell)

1.- Create a root trusted certificate:
$rootCert = New-SelfSignedCertificate -Subject 'CN=AskhatCertRoot,O=TestRootCA,OU=TestRootCA' -KeyExportPolicy Exportable -KeyUsage CertSign,CRLSign,DigitalSignature -KeyLength 2048 -KeyUsageProperty All -KeyAlgorithm 'RSA' -HashAlgorithm 'SHA256'  -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider'

2.- We create a certificate from a chain of trusted root certificates.:
New-SelfSignedCertificate -DnsName "habr.com" -FriendlyName "habr.com" -CertStoreLocation "cert:\LocalMachine\My" -Signer $rootCert -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") -Provider "Microsoft Strong Cryptographic Provider" -HashAlgorithm "SHA256" -NotAfter (Get-Date). AddYears(10)

3.- Copy the fingerprint returned by the last command

4.- We associate the new certificate with any ip and port 443 (the appid value does not matter, it is any valid guid):
netsh http add sslcert ipport=0.0.0.0:443 appid='{214124cd-d05b-4309-9af9-9caa44b2b74a}' certhash=HERE_INSERT_FINGERPRINT

5.- You should now open MMC (Certificates Local Computer) and drag the Certificates folder from Personal/Certificates to Trusted Root Certification Authorities/Certificates.. 

After that, you can configure your browser to proxy and make requests

ATTENTION. The Mozilla FaerFox browser cannot be used because it looks at its root CAs and not yours, and therefore it will still not allow the connection to be established

RUSSIAN
Инструкция к прокси серверу
Прокси сервер работает по HTTP и HTTPS протоколу. Для обработки запросов по HTTPS протоколу требует SSL сертификат установленный в Доверенные корневые центры сертификации. 
Сертификат должен называться так же как и домен к которому направлен запрос.
Ниже предоставления инструкция для создания сертификата к домену habr.com (выполнять комманды в PowerShell)

1.- Создаем корневой доверенный сертификат:
$rootCert = New-SelfSignedCertificate -Subject 'CN=AskhatCertRoot,O=TestRootCA,OU=TestRootCA' -KeyExportPolicy Exportable -KeyUsage CertSign,CRLSign,DigitalSignature -KeyLength 2048 -KeyUsageProperty All -KeyAlgorithm 'RSA' -HashAlgorithm 'SHA256'  -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider'

2.- Мы создаем сертификат из цепочки доверенных корневых сертификатов.:
New-SelfSignedCertificate -DnsName "habr.com" -FriendlyName "habr.com" -CertStoreLocation "cert:\LocalMachine\My" -Signer $rootCert -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") -Provider "Microsoft Strong Cryptographic Provider" -HashAlgorithm "SHA256" -NotAfter (Get-Date).AddYears(10)

3.- Копируем отпечаток, возвращенный последней командой

4.- Мы связываем новый сертификат с любым ip и портом 443 (значение appid не имеет значения, это любой допустимый guid):
netsh http add sslcert ipport=0.0.0.0:443 appid='{214124cd-d05b-4309-9af9-9caa44b2b74a}' certhash=СЮДА_ВСТАВИТЬ_ОТПЕЧАТОК

5.- Теперь вы должны открыть MMC (Certificates Local Computer) и перетащить папку сертификаты из Personal/Certificates в Trusted Root Certification Authorities/Certificates.. 

После можно настроить свой браузер на прокси и выполнять запросы

ВНИМАНИЕ. Браузер Mozilla FaerFox нельзя использовать т.к. он смотрит на свои корневые центры сертификации а не на ваши, и поэтому он все равно не будет позволять установить соединение

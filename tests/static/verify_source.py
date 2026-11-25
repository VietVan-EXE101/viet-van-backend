"""Static smoke checks — KHÔNG thay thế dotnet build/SQL integration test."""
from pathlib import Path
import json,re,subprocess,tempfile
root=Path(__file__).resolve().parents[2]
required={
    'scripts/PaymentUpgrade.sql':['CREATE TABLE dbo.PaymentOrders','CREATE TABLE dbo.PaymentWebhookEvents','CREATE UNIQUE INDEX UX_PWE_ProviderEvent','CREATE UNIQUE INDEX UX_PO_IdemKey'],
    'Services/Payments/PaymentService.cs':['ProcessVerifiedSePayAsync','sp_getapplock','BeginTransactionAsync','GrantedTierLevel','PaymentStates.RequiresReview'],
    'Controllers/PaymentController.cs':['api/payments/webhooks/sepay','ApiKey','Authorize','Admin'],
    'Controllers/DramaAccessController.cs':['PrivateMedia','SUBSCRIPTION_REQUIRED','PhysicalFile'],
    'Program.cs':['/drama-assets','UseAuthentication','UseAuthorization'],
}
for file,terms in required.items():
    txt=(root/file).read_text(encoding='utf8')
    for term in terms:assert term.lower() in txt.lower(),(file,term)
    print('PASS',file)
config=json.loads((root/'appsettings.json').read_text(encoding='utf8'))
assert 'WebhookApiKey' not in config.get('SePay',{}),'Source không lưu API key SePay'
assert config['Jwt']['Key']=='' , 'JWT secret phải đọc từ environment, không hardcode'
assert json.loads((root/'appsettings.Development.json').read_text())['DevelopmentAdmin']['Enabled'] is False
assert not (root/'wwwroot/drama-assets').exists(),'Không còn file drama public trong wwwroot'
print('PASS secrets placeholder and private media')
for path in (root/'wwwroot').glob('*.html'):
    for i,script in enumerate(re.findall(r'<script>([\s\S]*?)</script>',path.read_text(encoding='utf8'))):
        with tempfile.NamedTemporaryFile(mode='w',suffix='.js') as t:
            t.write(script);t.flush()
            result=subprocess.run(['node','--check',t.name],capture_output=True,text=True)
            assert result.returncode==0,(path,result.stderr)
    print('PASS JavaScript syntax',path.name)
print('PASS static checks. dotnet + SQL Server tests remain required.')

$key = 'C:\Users\rrathore\ssmsadmin.pem'
$vm = 'ssmsadmin@135.235.195.132'

scp -i $key "${vm}:~/smms/SMMS/api/SMMS.Api/Program.cs" .\prod-Program.cs

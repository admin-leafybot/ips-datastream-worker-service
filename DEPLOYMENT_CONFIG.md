# Deployment Configuration Guide

This document describes the required GitHub Secrets and environment variables for automated deployment.

## GitHub Secrets Required

Configure these secrets in your GitHub repository settings (`Settings` → `Secrets and variables` → `Actions`):

### AWS Configuration

| Secret Name | Description | Example |
|------------|-------------|---------|
| `AWS_REGION` | AWS region for deployment | `ap-south-1` |
| `AWS_ACCOUNT_ID` | Your AWS account ID | `123456789012` |
| `AWS_ACCESS_KEY_ID` | AWS access key for GitHub Actions | `AKIA...` |
| `AWS_SECRET_ACCESS_KEY` | AWS secret key for GitHub Actions | `wJalr...` |
| `ECR_REPOSITORY_DATASTREAM_WORKER` | ECR repository name | `ips-datastream-worker` |

### EC2 Deployment

| Secret Name | Description | Example |
|------------|-------------|---------|
| `EC2_HOST` | EC2 instance public IP or hostname | `ec2-xx-xx-xx-xx.compute.amazonaws.com` |
| `EC2_USER` | SSH username for EC2 | `ubuntu` or `ec2-user` |
| `EC2_SSH_KEY` | Private SSH key for EC2 access | `-----BEGIN RSA PRIVATE KEY-----...` |

### Application Configuration

| Secret Name | Description | Example |
|------------|-------------|---------|
| `DB_CONNECTION_STRING` | PostgreSQL connection string | `Host=xxx.rds.amazonaws.com;Port=5432;Database=ips_data_acquisition;Username=admin;Password=xxx;SSL Mode=Require` |
| `REDIS_ENDPOINT` | Redis endpoint with port | `xxx.cache.amazonaws.com:6379` or `xxx.cache.amazonaws.com:6379,password=xxx,ssl=true` |

## appsettings.Production.json Placeholders

The following placeholders in `appsettings.Production.json` will be automatically replaced during deployment:

```json
{
  "ConnectionStrings": {
    "Default": "__DB_CONNECTION_STRING__"  // ← Replaced with DB_CONNECTION_STRING secret
  },
  "Redis": {
    "Endpoint": "__REDIS_ENDPOINT__"       // ← Replaced with REDIS_ENDPOINT secret
  }
}
```

## Deployment Workflow

The GitHub Actions workflow (`deploy.yml`) performs these steps:

1. **Replace Placeholders**: Substitutes `__DB_CONNECTION_STRING__` and `__REDIS_ENDPOINT__` with actual secrets
2. **Build Docker Image**: Builds the application Docker image
3. **Push to ECR**: Pushes the image to Amazon ECR
4. **Prepare Compose File**: Replaces `__IMAGE_URI__` in `docker-compose.prod.yml` with the actual ECR image URI
5. **Upload to EC2**: Copies the prepared `docker-compose.prod.yml` to EC2
6. **Deploy**: Pulls the image and starts the container using docker-compose

## Manual Deployment Steps

If you need to deploy manually:

### 1. Build and Push Docker Image

```bash
# Authenticate with ECR
aws ecr get-login-password --region ap-south-1 | \
  docker login --username AWS --password-stdin <account-id>.dkr.ecr.ap-south-1.amazonaws.com

# Build image
docker build -t ips-datastream-worker:latest .

# Tag for ECR
docker tag ips-datastream-worker:latest \
  <account-id>.dkr.ecr.ap-south-1.amazonaws.com/ips-datastream-worker:latest

# Push to ECR
docker push <account-id>.dkr.ecr.ap-south-1.amazonaws.com/ips-datastream-worker:latest
```

### 2. Update docker-compose.prod.yml

Replace `__IMAGE_URI__` with your actual ECR image URI:

```bash
sed -i 's#__IMAGE_URI__#<account-id>.dkr.ecr.ap-south-1.amazonaws.com/ips-datastream-worker:latest#g' docker-compose.prod.yml
```

### 3. Deploy to EC2

```bash
# Copy compose file to EC2
scp docker-compose.prod.yml ubuntu@<ec2-host>:~/ips-datastream-worker-service/

# SSH into EC2 and deploy
ssh ubuntu@<ec2-host>
cd ~/ips-datastream-worker-service

# Login to ECR from EC2
aws ecr get-login-password --region ap-south-1 | \
  sudo docker login --username AWS --password-stdin <account-id>.dkr.ecr.ap-south-1.amazonaws.com

# Pull and start
sudo docker-compose -f docker-compose.prod.yml pull
sudo docker-compose -f docker-compose.prod.yml up -d

# Check logs
sudo docker-compose -f docker-compose.prod.yml logs -f
```

## Verifying Deployment

### Check Container Status

```bash
sudo docker ps | grep ips-datastream-worker
```

### View Logs

```bash
# Docker logs
sudo docker logs ips-datastream-worker -f

# CloudWatch logs (if configured)
aws logs tail /ecs/ips-datastream-worker --follow --region ap-south-1
```

### Check Database Connectivity

```bash
# Exec into container
sudo docker exec -it ips-datastream-worker /bin/bash

# Test database connection (inside container)
apt-get update && apt-get install -y postgresql-client
psql "<your-connection-string>" -c "SELECT COUNT(*) FROM sessions;"
```

### Check Redis Connectivity

```bash
# Exec into container
sudo docker exec -it ips-datastream-worker /bin/bash

# Test Redis connection (inside container)
apt-get update && apt-get install -y redis-tools
redis-cli -h <redis-host> -p 6379 ping
```

## Troubleshooting

### Issue: Workflow fails at "Replace placeholders"

**Cause**: Missing GitHub secrets or incorrect file paths

**Solution**: 
1. Verify all required secrets are configured
2. Check file paths in workflow match actual project structure

### Issue: Docker build fails

**Cause**: Dependency issues or build errors

**Solution**:
1. Test build locally first: `docker build -t test .`
2. Check Dockerfile syntax and project references
3. Ensure all .csproj files exist

### Issue: ECR push fails

**Cause**: Authentication or permission issues

**Solution**:
1. Verify AWS credentials have ECR push permissions
2. Ensure ECR repository exists
3. Check AWS region is correct

### Issue: Container starts but crashes

**Cause**: Configuration errors or runtime issues

**Solution**:
1. Check logs: `sudo docker logs ips-datastream-worker`
2. Verify connection strings are correct
3. Ensure database and Redis are accessible from EC2
4. Check security group rules

### Issue: No sessions are being processed

**Cause**: Database query criteria not met or configuration issues

**Solution**:
1. Verify sessions exist with correct status
2. Check `QualityCheck:PollingIntervalSeconds` setting
3. Review application logs for errors
4. Ensure database connection is working

## Environment-Specific Configuration

### Development

Uses `appsettings.Development.json` with localhost connections:
```bash
dotnet run --environment Development
```

### Docker (Local)

Uses `docker-compose.yml` with local PostgreSQL and Redis:
```bash
docker-compose up -d
```

### Production (EC2)

Uses `docker-compose.prod.yml` with production services:
- Environment variables injected by docker-compose
- Connects to RDS PostgreSQL
- Connects to ElastiCache Redis

## Security Best Practices

1. ✅ **Never commit secrets** to version control
2. ✅ **Use GitHub Secrets** for sensitive configuration
3. ✅ **Rotate credentials** regularly
4. ✅ **Use SSL/TLS** for database and Redis connections in production
5. ✅ **Restrict EC2 security groups** to allow only necessary traffic
6. ✅ **Use IAM roles** when possible instead of access keys
7. ✅ **Enable CloudWatch logs** for monitoring
8. ✅ **Set up alerts** for deployment failures

## Monitoring

### Key Metrics to Monitor

- Container CPU/Memory usage
- Database connection pool utilization
- Redis connection status
- Number of sessions processed per hour
- Quality check success/failure rate
- Error rate in logs

### CloudWatch Alarms (Recommended)

1. **High Error Rate**: Alert if error logs exceed threshold
2. **Container Health**: Alert if container stops
3. **Database Connections**: Alert if connection errors occur
4. **Redis Unavailable**: Alert if Redis connection fails

## Rollback Procedure

If deployment fails or causes issues:

```bash
# SSH into EC2
ssh ubuntu@<ec2-host>
cd ~/ips-datastream-worker-service

# Stop current container
sudo docker-compose -f docker-compose.prod.yml down

# Use previous image tag
# Edit docker-compose.prod.yml to use previous IMAGE_URI
nano docker-compose.prod.yml

# Start with previous version
sudo docker-compose -f docker-compose.prod.yml up -d
```

## Support

For deployment issues:
1. Check GitHub Actions logs
2. Review EC2 container logs
3. Verify all secrets are configured
4. Contact DevOps team

## Additional Resources

- [AWS ECR Documentation](https://docs.aws.amazon.com/ecr/)
- [GitHub Actions Documentation](https://docs.github.com/actions)
- [Docker Compose Documentation](https://docs.docker.com/compose/)


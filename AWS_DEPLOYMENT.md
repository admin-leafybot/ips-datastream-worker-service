# AWS Deployment Guide

This guide explains how to deploy the IPS Datastream Worker Service to AWS using **ECS (Elastic Container Service)** with **ECR (Elastic Container Registry)** for horizontally scalable, containerized deployment.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                          AWS Cloud                          │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │                    ECS Cluster                       │  │
│  │                                                      │  │
│  │  ┌──────────────────────────────────────────────┐  │  │
│  │  │  ECS Service (Worker)                        │  │  │
│  │  │  ┌────────┐  ┌────────┐  ┌────────┐         │  │  │
│  │  │  │ Task 1 │  │ Task 2 │  │ Task N │  ...    │  │  │
│  │  │  └────────┘  └────────┘  └────────┘         │  │  │
│  │  │  Auto-scaling based on CPU/Memory            │  │  │
│  │  └──────────────────────────────────────────────┘  │  │
│  │                                                      │  │
│  └──────────────────┬───────────────────────────────────┘  │
│                     │                                       │
│  ┌──────────────────▼───────────────────────────────────┐  │
│  │              ECR (Container Registry)                 │  │
│  │  - ips-datastream-worker:latest                      │  │
│  │  - ips-datastream-worker:v1.0.0                      │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  RDS PostgreSQL                │  ElastiCache Redis  │  │
│  │  - Multi-AZ                    │  - Cluster Mode     │  │
│  │  - Automated Backups           │  - HA               │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  CloudWatch                                           │  │
│  │  - Logs from ECS Tasks                               │  │
│  │  - Metrics & Alarms                                  │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Prerequisites

- AWS Account with appropriate permissions
- AWS CLI configured (`aws configure`)
- Docker installed locally
- .NET 9.0 SDK

## Step 1: Create ECR Repository

### Using AWS Console

1. Navigate to **ECR** → **Repositories**
2. Click **Create repository**
3. Repository name: `ips-datastream-worker`
4. Enable **Scan on push** for security
5. Click **Create repository**

### Using AWS CLI

```bash
aws ecr create-repository \
    --repository-name ips-datastream-worker \
    --image-scanning-configuration scanOnPush=true \
    --region us-east-1
```

## Step 2: Build and Push Docker Image

### 1. Authenticate Docker with ECR

```bash
aws ecr get-login-password --region us-east-1 | \
    docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com
```

### 2. Build Docker Image

```bash
docker build -t ips-datastream-worker:latest .
```

### 3. Tag Image for ECR

```bash
docker tag ips-datastream-worker:latest \
    <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:latest

docker tag ips-datastream-worker:latest \
    <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:v1.0.0
```

### 4. Push Image to ECR

```bash
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:latest
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:v1.0.0
```

## Step 3: Create ECS Cluster

### Using AWS Console

1. Navigate to **ECS** → **Clusters**
2. Click **Create cluster**
3. Cluster name: `ips-production-cluster`
4. Infrastructure: **AWS Fargate** (serverless) or **EC2** (more control)
5. Click **Create**

### Using AWS CLI

```bash
aws ecs create-cluster \
    --cluster-name ips-production-cluster \
    --region us-east-1
```

## Step 4: Create Task Definition

Create `task-definition.json`:

```json
{
  "family": "ips-datastream-worker",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "1024",
  "memory": "2048",
  "executionRoleArn": "arn:aws:iam::<account-id>:role/ecsTaskExecutionRole",
  "taskRoleArn": "arn:aws:iam::<account-id>:role/ecsTaskRole",
  "containerDefinitions": [
    {
      "name": "datastream-worker",
      "image": "<account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:latest",
      "essential": true,
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/ips-datastream-worker",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "worker"
        }
      },
      "environment": [
        {
          "name": "DOTNET_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "QualityCheck__PollingIntervalSeconds",
          "value": "30"
        },
        {
          "name": "QualityCheck__CompletedThresholdMinutes",
          "value": "5"
        },
        {
          "name": "QualityCheck__BatchSize",
          "value": "20"
        }
      ],
      "secrets": [
        {
          "name": "ConnectionStrings__Default",
          "valueFrom": "arn:aws:secretsmanager:us-east-1:<account-id>:secret:ips/database-connection"
        },
        {
          "name": "ConnectionStrings__Redis",
          "valueFrom": "arn:aws:secretsmanager:us-east-1:<account-id>:secret:ips/redis-connection"
        }
      ]
    }
  ]
}
```

### Register Task Definition

```bash
aws ecs register-task-definition \
    --cli-input-json file://task-definition.json \
    --region us-east-1
```

## Step 5: Store Secrets in AWS Secrets Manager

```bash
# Store database connection string
aws secretsmanager create-secret \
    --name ips/database-connection \
    --secret-string "Host=your-rds-endpoint;Port=5432;Database=ips_data_acquisition;Username=admin;Password=YourPassword;SSL Mode=Require" \
    --region us-east-1

# Store Redis connection string
aws secretsmanager create-secret \
    --name ips/redis-connection \
    --secret-string "your-elasticache-endpoint:6379,ssl=true" \
    --region us-east-1
```

## Step 6: Create CloudWatch Log Group

```bash
aws logs create-log-group \
    --log-group-name /ecs/ips-datastream-worker \
    --region us-east-1
```

## Step 7: Create ECS Service

### Using AWS Console

1. Navigate to your ECS cluster
2. Click **Create** under Services
3. Configuration:
   - Launch type: **Fargate**
   - Task definition: `ips-datastream-worker`
   - Service name: `datastream-worker-service`
   - Number of tasks: `2` (or more for HA)
4. Networking:
   - VPC: Select your VPC
   - Subnets: Select private subnets
   - Security group: Allow outbound to RDS (5432) and Redis (6379)
5. Auto-scaling:
   - Enable auto-scaling
   - Min tasks: `2`
   - Max tasks: `10`
   - Target metric: CPU or Memory utilization (e.g., 70%)
6. Click **Create**

### Using AWS CLI

```bash
aws ecs create-service \
    --cluster ips-production-cluster \
    --service-name datastream-worker-service \
    --task-definition ips-datastream-worker \
    --desired-count 2 \
    --launch-type FARGATE \
    --network-configuration "awsvpcConfiguration={subnets=[subnet-xxx,subnet-yyy],securityGroups=[sg-xxx],assignPublicIp=DISABLED}" \
    --region us-east-1
```

## Step 8: Configure Auto-Scaling

Create auto-scaling policy:

```bash
# Register scalable target
aws application-autoscaling register-scalable-target \
    --service-namespace ecs \
    --resource-id service/ips-production-cluster/datastream-worker-service \
    --scalable-dimension ecs:service:DesiredCount \
    --min-capacity 2 \
    --max-capacity 10 \
    --region us-east-1

# Create scaling policy (CPU-based)
aws application-autoscaling put-scaling-policy \
    --service-namespace ecs \
    --resource-id service/ips-production-cluster/datastream-worker-service \
    --scalable-dimension ecs:service:DesiredCount \
    --policy-name cpu-scaling-policy \
    --policy-type TargetTrackingScaling \
    --target-tracking-scaling-policy-configuration file://scaling-policy.json \
    --region us-east-1
```

`scaling-policy.json`:
```json
{
  "TargetValue": 70.0,
  "PredefinedMetricSpecification": {
    "PredefinedMetricType": "ECSServiceAverageCPUUtilization"
  },
  "ScaleInCooldown": 300,
  "ScaleOutCooldown": 60
}
```

## Step 9: Monitoring & Logging

### View Logs

```bash
# Stream logs
aws logs tail /ecs/ips-datastream-worker --follow --region us-east-1
```

### CloudWatch Metrics

Monitor these key metrics:
- **CPUUtilization**: Average CPU usage across tasks
- **MemoryUtilization**: Average memory usage
- **TaskCount**: Number of running tasks

### Create Alarms

```bash
aws cloudwatch put-metric-alarm \
    --alarm-name ips-datastream-worker-high-cpu \
    --alarm-description "Alert when worker CPU is too high" \
    --metric-name CPUUtilization \
    --namespace AWS/ECS \
    --statistic Average \
    --period 300 \
    --threshold 90 \
    --comparison-operator GreaterThanThreshold \
    --evaluation-periods 2 \
    --dimensions Name=ServiceName,Value=datastream-worker-service Name=ClusterName,Value=ips-production-cluster \
    --alarm-actions arn:aws:sns:us-east-1:<account-id>:admin-alerts \
    --region us-east-1
```

## Step 10: CI/CD Pipeline (Optional)

### Using GitHub Actions

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to ECS

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Configure AWS credentials
        uses: aws-actions/configure-aws-credentials@v2
        with:
          aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          aws-region: us-east-1
      
      - name: Login to Amazon ECR
        id: login-ecr
        uses: aws-actions/amazon-ecr-login@v1
      
      - name: Build, tag, and push image
        env:
          ECR_REGISTRY: ${{ steps.login-ecr.outputs.registry }}
          ECR_REPOSITORY: ips-datastream-worker
          IMAGE_TAG: ${{ github.sha }}
        run: |
          docker build -t $ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG .
          docker push $ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG
          docker tag $ECR_REGISTRY/$ECR_REPOSITORY:$IMAGE_TAG $ECR_REGISTRY/$ECR_REPOSITORY:latest
          docker push $ECR_REGISTRY/$ECR_REPOSITORY:latest
      
      - name: Update ECS service
        run: |
          aws ecs update-service \
            --cluster ips-production-cluster \
            --service datastream-worker-service \
            --force-new-deployment \
            --region us-east-1
```

## Updating the Service

### Deploy New Version

```bash
# Build and push new image
docker build -t ips-datastream-worker:v1.1.0 .
docker tag ips-datastream-worker:v1.1.0 <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:v1.1.0
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/ips-datastream-worker:v1.1.0

# Update task definition (change image tag)
# Re-register task definition
aws ecs register-task-definition --cli-input-json file://task-definition.json

# Update service to use new task definition
aws ecs update-service \
    --cluster ips-production-cluster \
    --service datastream-worker-service \
    --task-definition ips-datastream-worker:2 \
    --force-new-deployment \
    --region us-east-1
```

## Cost Optimization

1. **Right-size tasks**: Start with smaller CPU/memory, scale up as needed
2. **Use Fargate Spot**: Save up to 70% for fault-tolerant workloads
3. **Schedule scaling**: Reduce tasks during off-peak hours
4. **Monitor unused resources**: Use AWS Cost Explorer

## Troubleshooting

### Tasks Keep Stopping

Check:
- CloudWatch logs for errors
- Task IAM role has permissions
- Secrets Manager secrets are accessible
- Network connectivity to RDS/Redis

### High CPU/Memory

- Increase task CPU/memory
- Optimize polling interval
- Reduce batch size
- Add more tasks (horizontal scaling)

### No Sessions Processed

- Verify database connectivity
- Check Redis connectivity
- Ensure sessions exist with correct criteria
- Review application logs

## Security Best Practices

1. ✅ Store secrets in AWS Secrets Manager
2. ✅ Use IAM roles (not access keys)
3. ✅ Run tasks in private subnets
4. ✅ Enable encryption at rest (RDS, ElastiCache)
5. ✅ Enable encryption in transit (SSL/TLS)
6. ✅ Use security groups to restrict access
7. ✅ Enable ECR image scanning
8. ✅ Regularly update base images
9. ✅ Enable CloudTrail for audit logs
10. ✅ Use least-privilege IAM policies

## Support

For AWS-specific issues, consult:
- [ECS Documentation](https://docs.aws.amazon.com/ecs/)
- [ECR Documentation](https://docs.aws.amazon.com/ecr/)
- [AWS Support](https://console.aws.amazon.com/support/)


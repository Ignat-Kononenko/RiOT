def runCmd(String cmd) {
    if (isUnix()) {
        sh cmd
    } else {
        bat cmd
    }
}

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
    }

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        SERVER_PROJECT = 'Server/Server.csproj'
        TESTS_PROJECT = 'FitnessServer.Tests/FitnessServer.Tests.csproj'
        IMAGE_NAME = 'riot-server'
        DEPLOYMENT_NAME = 'fitness-server'
        CONTAINER_NAME = 'server'
    }

    stages {
        stage('Checkout') {
            steps { checkout scm }
        }

        stage('Restore') {
            steps {
                script {
                    runCmd "dotnet restore ${env.SERVER_PROJECT}"
                    runCmd "dotnet restore ${env.TESTS_PROJECT}"
                }
            }
        }

        stage('Build') {
            steps {
                script {
                    runCmd "dotnet build ${env.SERVER_PROJECT} --configuration Release --no-restore"
                    runCmd "dotnet build ${env.TESTS_PROJECT} --configuration Release --no-restore"
                }
            }
        }

        stage('Start DB') {
            steps {
                script {
                    // 1. Принудительно удаляем старый контейнер (если он есть)
                    if (isUnix()) {
                        sh "docker rm -f fitness-mssql || true"
                    } else {
                        bat "docker rm -f fitness-mssql 2>NUL || exit 0"
                    }
                    
                    // 2. Поднимаем базу данных заново
                    runCmd "docker compose up -d mssql"
                    
                    // 3. Ждем запуска
                    if (isUnix()) {
                        sh "sleep 20"
                    } else {
                        bat "powershell -Command \"Start-Sleep -Seconds 20\""
                    }
                }
            }
        }

        stage('Test') {
            steps {
                script {
                    runCmd "dotnet test ${env.TESTS_PROJECT} --configuration Release --no-build --no-restore"
                }
            }
            post {
                always {
                    if (isUnix()) {
                        sh "docker rm -f fitness-mssql || true"
                    } else {
                        bat "docker rm -f fitness-mssql 2>NUL || exit 0"
                    }
                }
            }
        }

        stage('Package') {
            steps {
                script {
                    dir('Server') {
                        runCmd "docker build -t ${env.IMAGE_NAME}:${env.BUILD_NUMBER} ."
                    }
                    runCmd "docker images ${env.IMAGE_NAME}"
                }
            }
        }

        stage('Deploy') {
            steps {
                script {
                    runCmd "kubectl set image deployment/${env.DEPLOYMENT_NAME} ${env.CONTAINER_NAME}=${env.IMAGE_NAME}:${env.BUILD_NUMBER}"
                    runCmd "kubectl rollout status deployment/${env.DEPLOYMENT_NAME} --timeout=120s"
                    runCmd "kubectl get pods"
                }
            }
        }
    }

    post {
        success {
            echo "Pipeline успешно завершен! Приложение развернуто в Kubernetes."
        }
        failure {
            echo "Pipeline завершился с ошибкой. Проверьте логи."
        }
    }
}
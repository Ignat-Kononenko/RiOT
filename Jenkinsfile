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
        // Добавляем пароль на всякий случай прямо в окружение
        MSSQL_SA_PASSWORD = 'YourStrong@Password123' 
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
                    // ВАЖНО: Замените "sqlserver" на точное имя сервиса БД из вашего compose.yaml!
                    // Если их два, напишите через пробел: docker compose up -d db-primary db-replica
                    runCmd "docker compose up -d sqlserver"
                    
                    if (isUnix()) {
                        sh "sleep 15"
                    } else {
                        bat "powershell -Command \"Start-Sleep -Seconds 15\""
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
                    runCmd "docker compose down"
                }
            }
        }

        stage('Package') {
            steps {
                script {
                    dir('Server') {
                        runCmd "docker build -t ${env.IMAGE_NAME}:${env.BUILD_NUMBER} ."
                    }
                }
            }
        }
    }
}
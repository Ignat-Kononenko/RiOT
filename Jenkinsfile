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
        DEPLOYMENT_NAME = 'fitness-server'  // Имя вашего Deployment в K8s
        CONTAINER_NAME = 'server'           // Имя контейнера внутри Deployment
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
                    runCmd "docker compose down"
                    runCmd "docker compose up -d mssql"
                    
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
                    // Останавливаем БД, так как тесты прошли (или упали)
                    runCmd "docker compose down"
                }
            }
        }

        stage('Package') {
            steps {
                script {
                    // Собираем Docker-образ с уникальным тегом (номером сборки)
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
                    // Обновляем образ в Kubernetes
                    runCmd "kubectl set image deployment/${env.DEPLOYMENT_NAME} ${env.CONTAINER_NAME}=${env.IMAGE_NAME}:${env.BUILD_NUMBER}"
                    
                    // Ждем, пока Kubernetes обновит поды
                    runCmd "kubectl rollout status deployment/${env.DEPLOYMENT_NAME} --timeout=120s"
                    
                    // Проверяем, что поды запустились
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
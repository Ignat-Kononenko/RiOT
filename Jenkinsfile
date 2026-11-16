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
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
            }
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

        stage('Test') {
            steps {
                script {
                    runCmd "dotnet test ${env.TESTS_PROJECT} --configuration Release --no-build --no-restore --logger \"trx;LogFileName=test_results.trx\""
                }
            }
            post {
                always {
                    junit allowEmptyResults: true, testResults: '**/TestResults/*.trx'
                }
            }
        }

        stage('Package') {
            steps {
                script {
                    // Сборка образа. Контекст - папка Server, Dockerfile лежит там же
                    runCmd "docker build -t ${env.IMAGE_NAME}:${env.BUILD_NUMBER} -f Server/Dockerfile Server"
                    
                    // Вывод списка образов для проверки
                    runCmd "docker images ${env.IMAGE_NAME}"
                }
            }
        }
    }

    post {
        success {
            echo "Pipeline и создание Docker-образа завершены успешно."
        }
        failure {
            echo "Pipeline завершился с ошибкой. Проверьте логи."
        }
    }
}
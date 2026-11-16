IF DB_ID('FitnessDB') IS NULL
    CREATE DATABASE FitnessDB;
GO

SELECT name FROM sys.databases WHERE name = 'FitnessDB';
SELECT name FROM sys.tables;

USE FitnessDB;
SELECT * FROM Trainers;
SELECT * FROM Clients;
SELECT * FROM TrainingSlots;
SELECT * FROM Trainings;

SELECT * FROM Memberships;
SELECT * FROM Payments;


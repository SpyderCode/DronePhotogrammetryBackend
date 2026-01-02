-- Database Setup Script for Photogrammetry API
-- Run this script to create the database and tables

CREATE DATABASE IF NOT EXISTS photogrammetry;
USE photogrammetry;

-- Users table
CREATE TABLE IF NOT EXISTS Users (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Username VARCHAR(100) NOT NULL,
    Email VARCHAR(255) NOT NULL,
    PasswordHash TEXT NOT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE INDEX IX_Users_Email (Email),
    UNIQUE INDEX IX_Users_Username (Username)
);

-- Projects table
CREATE TABLE IF NOT EXISTS Projects (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    UserId INT NOT NULL,
    ZipFilePath VARCHAR(500) NOT NULL,
    OutputModelPath VARCHAR(500) NULL,
    Status INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ProcessingStartedAt DATETIME NULL,
    CompletedAt DATETIME NULL,
    ErrorMessage VARCHAR(1000) NULL,
    FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
    INDEX IX_Projects_UserId (UserId)
);

-- Status enum values:
-- 0 = InQueue
-- 1 = Processing
-- 2 = Finished
-- 3 = Failed

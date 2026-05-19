-- Create password_reset_tokens table for password reset functionality
-- This table stores one-time, short-lived tokens for password reset requests

CREATE TABLE password_reset_tokens (
    id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    user_id UNIQUEIDENTIFIER NOT NULL,
    token_hash NVARCHAR(255) NOT NULL,
    expires_at DATETIMEOFFSET NOT NULL,
    consumed_at DATETIMEOFFSET NULL,
    created_at DATETIMEOFFSET NOT NULL DEFAULT GETUTCDATE(),
    
    CONSTRAINT FK_password_reset_tokens_users FOREIGN KEY (user_id) REFERENCES users(id),
    CONSTRAINT CK_password_reset_tokens_expires CHECK (expires_at > created_at)
);

-- Create index on token_hash for fast lookups
CREATE INDEX IX_password_reset_tokens_token_hash ON password_reset_tokens(token_hash);

-- Create index on user_id for cleanup operations
CREATE INDEX IX_password_reset_tokens_user_id ON password_reset_tokens(user_id);

-- Create index on expires_at for cleanup of expired tokens
CREATE INDEX IX_password_reset_tokens_expires_at ON password_reset_tokens(expires_at) WHERE consumed_at IS NULL;

GO

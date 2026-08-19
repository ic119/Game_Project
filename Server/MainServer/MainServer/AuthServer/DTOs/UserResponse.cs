using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainServer.AuthServer.DTOs
{
    public record UserResponse(long _id, string _username, string _nickname, DateTime _createdAt);
}
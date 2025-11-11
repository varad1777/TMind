using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyApp.Application.Dtos
{
   
        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public T? Data { get; set; }
            public string? Error { get; set; }

            public static ApiResponse<T> Ok(T? data) => new ApiResponse<T> { Success = true, Data = data, Error = null };
            public static ApiResponse<T> Fail(string error) => new ApiResponse<T> { Success = false, Data = default, Error = error };
        }
    


}

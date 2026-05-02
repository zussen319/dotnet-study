var filePath = "ResponseData.json";
List<Response> responses = await Response.LoadResponsesAsync(filePath);
responses.ForEach(x => x.Print());

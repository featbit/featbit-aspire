using FeatBit.Sdk.Server;
using FeatBit.Sdk.Server.Model;
using FeatBit.Sdk.Server.Options;

// setup SDK options
//var options = new FbOptionsBuilder("")
//    .Event(new Uri("https://featbit-evaluation-server.delightfulcoast-374f34c4.westus2.azurecontainerapps.io"))
//    .Streaming(new Uri("wss://featbit-evaluation-server.delightfulcoast-374f34c4.westus2.azurecontainerapps.io"))
//    .Build();
var options = new FbOptionsBuilder("")
    .Event(new Uri("http://localhost:5100"))
    .Streaming(new Uri("ws://localhost:5100"))
    .Build();


// Creates a new client instance that connects to FeatBit with the custom option.
var client = new FbClient(options);
if (!client.Initialized)
{
    Console.WriteLine("FbClient failed to initialize. All Variation calls will use fallback value.");
}
else
{
    Console.WriteLine("FbClient successfully initialized!");
}

// flag to be evaluated
const string flagKey = "game-runner";

// create a user
var user = FbUser.Builder("anonymous").Build();

// evaluate a boolean flag for a given user
var boolVariation = client.BoolVariation(flagKey, user, defaultValue: false);
Console.WriteLine($"flag '{flagKey}' returns {boolVariation} for user {user.Key}");

// evaluate a boolean flag for a given user with evaluation detail
var boolVariationDetail = client.BoolVariationDetail(flagKey, user, defaultValue: false);
Console.WriteLine(
    $"flag '{flagKey}' returns {boolVariationDetail.Value} for user {user.Key}. " +
    $"Reason Kind: {boolVariationDetail.Kind}, Reason Description: {boolVariationDetail.Reason}"
);

// close the client to ensure that all insights are sent out before the app exits
await client.CloseAsync();
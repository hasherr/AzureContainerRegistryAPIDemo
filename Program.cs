﻿using Flurl.Http;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AzureContainerRegistryAPIDemo
{
    internal class Program
    {
        async static Task Main(string[] args)
        {
            // Hardcoded ACR credentials
            string basicCredentialsBase64 = ""; // Created with the following command: echo -n [registry username]:[registry password] | base64
            string azureCredentialToken = "";

            // Registry & Repository Information
            string registryUrl = "myregistry.azurecr.io";
            string repositoryName = "hello-world";
            string registryScope = "repository:hello-world:pull,push";

            // Locations on machine.
            // The image being uploaded is cobbled together using a docker/podman 'save' command. 
            string pathToLayer = @"C:\Users\DemoUser\ContainerImages\hello-world\";
            string layerDigest = "sha256:feb5d9fea6a5e9606aa995e879d862b825965ba48de054caab5ef356dc6b3412";

            // Get authentication token from Azure Container Registry.
            AuthenticationResponse response = await $"https://{registryUrl}/oauth2/token?service={registryUrl}&scope={registryScope}"
                                                .WithHeader("Authorization", $"Basic {basicCredentialsBase64}")
                                                .GetAsync()
                                                .ReceiveJson<AuthenticationResponse>();
            azureCredentialToken = response.AccessToken;

            /** As an example, this demonstrates the upload of a single image with a single layer (as in the docker.io/library/hello-world image) **/

            // Begin upload of layer as BLOB.
            // Expected result: Status code 202, "Location" header displays upload location for subsequent BLOB uploads.
            // Actual result: Status code 202, "Location" header displays upload location for subsequent BLOB uploads.
            string nextUploadLocation = "";
            var startBlobUploadResponse = await $"https://{registryUrl}/v2/{repositoryName}/blobs/uploads/"
                            .WithHeader("Authorization", $"Bearer {azureCredentialToken}")
                            .WithHeader("Access-Control-Expose-Headers", "Docker-Content-Digest")
                            .WithHeader("Accept", "application/vnd.docker.distribution.manifest.v2+json")
                            .PostAsync();
            startBlobUploadResponse.Headers.TryGetFirst("Location", out nextUploadLocation);

            if (startBlobUploadResponse.StatusCode == 202 && !string.IsNullOrEmpty(nextUploadLocation))
            {
                if (File.Exists(pathToLayer))
                {
                    // Write file to a buffer array
                    // Convert buffer value to Base64 and store in string for BLOB upload.
                    int chunkSize = 65_536;
                    BinaryReader binaryReader = new BinaryReader(new FileStream(pathToLayer, FileMode.Open, FileAccess.Read));
                    byte[] buffer = binaryReader.ReadBytes(chunkSize);

                    string chunkValue = Convert.ToBase64String(buffer, 0, buffer.Length);

                    // Uploads BLOB information using previously acquired "Location" header.
                    // Expected result: Status code 202, "Location" header displays upload location for subsequent BLOB uploads.
                    // Actual result: Status code 202, "Location" header displays upload location for subsequent BLOB uploads.
                    string blobUploadEndpoint = $"https://{registryUrl}{nextUploadLocation}";
                    var blobUploadResponse = await blobUploadEndpoint
                        .WithHeader("Authorization", $"Bearer {azureCredentialToken}")
                        .WithHeader("Accept", "application/vnd.oci.image.manifest.v2+json")
                        .WithHeader("Accept", "application/vnd.docker.distribution.manifest.v2+json")
                        .WithHeader("Content-Length", buffer.Length)
                        .WithHeader("Content-Type", "application/octet-stream")
                        .PatchStringAsync(chunkValue);
                    blobUploadResponse.Headers.TryGetFirst("Location", out nextUploadLocation);

                    // Finish BLOB upload.
                    // Expected result: 201 result. "Location" header displays location of BLOB upload.
                    // Actual result: 400 error. Following this example using CURL sometimes displays additional information, specifically: "Invalid digest",
                    // though recent debug attempts yield no additional detail at all.
                    var finishUploadResponse = $"https://{registryUrl}{nextUploadLocation}&digest={layerDigest}"
                                                .WithHeader("Authorization", $"Bearer {azureCredentialToken}")
                                                .PutAsync();
                }
            }
        }
    }

    class AuthenticationResponse
    {
        [JsonProperty(PropertyName = "access_token")]
        public string AccessToken { set; internal get; }
    }
}
